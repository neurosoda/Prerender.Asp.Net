using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.PhantomJS;

namespace Prerender.Asp.Net
{
    public class PrerenderModule : IHttpModule
    {
        private PrerenderConfigSection _prerenderConfig;
        private HttpApplication _context;
        private static readonly string PRERENDER_SECTION_KEY = "prerender";
        private static readonly string _Escaped_Fragment = "_escaped_fragment_";

        public void Dispose()
        {

        }

        public void Init(HttpApplication context)
        {
            this._context = context;
            _prerenderConfig = ConfigurationManager.GetSection(PRERENDER_SECTION_KEY) as PrerenderConfigSection;

            context.BeginRequest += context_BeginRequest;
        }

        protected void context_BeginRequest(object sender, EventArgs e)
        {
            try
            {
                DoPrerender(_context);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
            }
        }

        private void DoPrerender(HttpApplication context)
        {
            var httpContext = context.Context;
            var request = httpContext.Request;
            var response = httpContext.Response;
            if (ShouldShowPrerenderedPage(request))
            {
                var result = GetPrerenderedPageResponse(request);

                response.StatusCode = (int)result.StatusCode;

                // The WebHeaderCollection is horrible, so we enumerate like this!
                // We are adding the received headers from the prerender service
                for (var i = 0; i < result.Headers.Count; ++i)
                {
                    var header = result.Headers.GetKey(i);
                    var values = result.Headers.GetValues(i);

                    if (values == null) continue;

                    foreach (var value in values)
                    {
                        response.Headers.Add(header, value);
                    }
                }

                response.Write(result.ResponseBody);
                response.Flush();
                context.CompleteRequest();
            }
        }

        private ResponseResult GetPrerenderedPageResponse(HttpRequest request)
        {
            IWebDriver driver = null;
            try
            {
                var driverService = PhantomJSDriverService.CreateDefaultService();
                driverService.HideCommandPromptWindow = true;

                driver = new PhantomJSDriver(driverService);

                driver.Manage().Timeouts().SetPageLoadTimeout(TimeSpan.FromSeconds(60));
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(60);
                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
                driver.Manage().Timeouts().SetScriptTimeout(TimeSpan.FromSeconds(60));

                string url = request.Url.AbsoluteUri;

                driver.Navigate().GoToUrl(url);

                Thread.Sleep(3000);

                //var reader = new StreamReader(driver.PageSource, Encoding.UTF8);
                return new ResponseResult(HttpStatusCode.OK, driver.PageSource.ToString(), new WebHeaderCollection());
            }
            catch (Exception exception)
            {
                return new ResponseResult(HttpStatusCode.BadRequest, "", new WebHeaderCollection());
            }
            finally
            {
                if (driver != null)
                {
                    driver.Quit();
                    driver.Close();
                }

            }
        }

        private void SetProxy(HttpWebRequest webRequest)
        {
            if (_prerenderConfig.Proxy != null && _prerenderConfig.Proxy.Url.IsNotBlank())
            {
                webRequest.Proxy = new WebProxy(_prerenderConfig.Proxy.Url, _prerenderConfig.Proxy.Port);
            }
        }

        private static void SetNoCache(HttpWebRequest webRequest)
        {
            webRequest.Headers.Add("Cache-Control", "no-cache");
            webRequest.ContentType = "text/html";
        }


        public static string RemoveQueryStringByKey(string url, string key)
        {
            var uri = new Uri(url);

            // this gets all the query string key value pairs as a collection
            var newQueryString = HttpUtility.ParseQueryString(uri.Query);

            // this removes the key if exists
            newQueryString.Remove(key);

            // this gets the page path from root without QueryString
            string pagePathWithoutQueryString = uri.GetLeftPart(UriPartial.Path);

            return newQueryString.Count > 0
                ? String.Format("{0}?{1}", pagePathWithoutQueryString, newQueryString)
                : pagePathWithoutQueryString;
        }


        private bool ShouldShowPrerenderedPage(HttpRequest request)
        {
            var userAgent = request.UserAgent;
            var url = request.Url;
            var referer = request.UrlReferrer == null ? string.Empty : request.UrlReferrer.AbsoluteUri;

            var blacklist = _prerenderConfig.Blacklist;
            if (blacklist != null && IsInBlackList(url, referer, blacklist))
            {
                return false;
            }

            var whiteList = _prerenderConfig.Whitelist;
            if (whiteList != null && !IsInWhiteList(url, whiteList))
            {
                return false;
            }

            if (HasEscapedFragment(request))
            {
                return true;
            }
            if (userAgent.IsBlank())
            {
                return false;
            }

            if (!IsInSearchUserAgent(userAgent))
            {
                return false;
            }
            if (IsInResources(url))
            {
                return false;
            }
            return true;

        }

        private bool IsInBlackList(Uri url, string referer, IEnumerable<string> blacklist)
        {
            return blacklist.Any(item =>
            {
                var regex = new Regex(item);
                return regex.IsMatch(url.AbsoluteUri) || (referer.IsNotBlank() && regex.IsMatch(referer));
            });
        }

        private bool IsInWhiteList(Uri url, IEnumerable<string> whiteList)
        {
            return whiteList.Any(item => new Regex(item).IsMatch(url.AbsoluteUri));
        }

        private bool IsInResources(Uri url)
        {
            var extensionsToIgnore = GetExtensionsToIgnore();
            return extensionsToIgnore.Any(item => url.AbsoluteUri.ToLower().Contains(item.ToLower()));
        }

        private IEnumerable<String> GetExtensionsToIgnore()
        {
            var extensionsToIgnore = new List<string>(new[]{".js", ".css", ".less", ".png", ".jpg", ".jpeg",
                ".gif", ".pdf", ".doc", ".txt", ".zip", ".mp3", ".rar", ".exe", ".wmv", ".doc", ".avi", ".ppt", ".mpg",
                ".mpeg", ".tif", ".wav", ".mov", ".psd", ".ai", ".xls", ".mp4", ".m4a", ".swf", ".dat", ".dmg",
                ".iso", ".flv", ".m4v", ".torrent"});
            if (_prerenderConfig.ExtensionsToIgnore.IsNotEmpty())
            {
                extensionsToIgnore.AddRange(_prerenderConfig.ExtensionsToIgnore);
            }
            return extensionsToIgnore;
        }

        private bool IsInSearchUserAgent(string useAgent)
        {
            var crawlerUserAgents = GetCrawlerUserAgents();

            // We need to see if the user agent actually contains any of the partial user agents we have!
            // THE ORIGINAL version compared for an exact match...!
            return
                (crawlerUserAgents.Any(
                    crawlerUserAgent =>
                    useAgent.IndexOf(crawlerUserAgent, StringComparison.InvariantCultureIgnoreCase) >= 0));
        }

        private IEnumerable<String> GetCrawlerUserAgents()
        {
            var crawlerUserAgents = new List<string>(new[]
                {
                    "googlebot", "yahoo", "bingbot", "yandex", "baiduspider", "facebookexternalhit", "twitterbot", "rogerbot", "linkedinbot", 
                    "embedly", "quora link preview", "showyoubot", "outbrain", "pinterest/0.", 
                    "developers.google.com/+/web/snippet", "slackbot", "vkShare", "W3C_Validator", 
                    "redditbot", "Applebot", "WhatsApp", "flipboard", "tumblr", "bitlybot", 
                    "SkypeUriPreview", "nuzzel", "Discordbot", "Google Page Speed", "x-bufferbot"
                });

            if (_prerenderConfig.CrawlerUserAgents.IsNotEmpty())
            {
                crawlerUserAgents.AddRange(_prerenderConfig.CrawlerUserAgents);
            }
            return crawlerUserAgents;
        }

        private bool HasEscapedFragment(HttpRequest request)
        {
            return request.QueryString.AllKeys.Contains(_Escaped_Fragment);
        }

    }
}
