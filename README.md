Prerender.Asp.Net it is web-pages prerender for ASP.NET projects
============================================

Are you using backbone, angular, emberjs, etc, but you're unsure about the SEO implications?

Use this lib that prerenders a javascript-rendered page using an external service and returns the HTML to the search engine crawler for SEO.

`Note:` If you are using a `#` in your urls, make sure to change it to `#!`. [View Google's ajax crawling protocol](https://developers.google.com/webmasters/ajax-crawling/docs/getting-started)

`Note:` Make sure you have more than one webserver thread/process running because the prerender service will make a request to your server to render the HTML.

## Installing

1: Do a build of this project and include and reference the DLL in your web application

2: Add the http module to your web.config:

	<httpModules>
		<add name="Prerender" type="Prerender.Asp.Net.PrerenderModule, Prerender.Asp.Net, Version=1.0.0.2, Culture=neutral, PublicKeyToken=null"/>
	</httpModules>

3: You can add following additional attributes to the prerender section to override or add to the custom settings (see PrerenderModule.cs):

  - stripApplicationNameFromRequestUrl
  - whitelist
  - blacklist
  - extensionsToIgnore
  - crawlerUserAgents

4: Create a new class called PreApplicationStartCode in the App_Start folder:

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Web.Infrastructure.DynamicModuleHelper;
    using Microsoft.Web.WebPages.OAuth;
    using Demo.Models;
    
    namespace Demo
    {
        public static class PreApplicationStartCode
        {
            private static bool _isStarting;
    
            public static void PreStart()
            {
                if (!_isStarting)
                {
                    _isStarting = true;
    
                    DynamicModuleUtility.RegisterModule(typeof(Prerender.Asp.Net.PrerenderModule));
                }
            }
        }
    }

5: Add this line to the bottom of the AssemblyInfo.cs file:
```
[assembly: PreApplicationStartMethod(typeof(Demo.PreApplicationStartCode), "PreStart")]
```

6: Build and publish you web application. 

##### Mac:
  1. Open the Developer Tools in Chrome (Cmd + Atl + J)
  2. Click the Settings gear in the bottom right corner.
  3. Click "Overrides" on the left side of the settings panel.
  4. Check the "User Agent" checkbox.
  6. Choose "Other..." from the User Agent dropdown.
  7. Type `googlebot` into the input box.
  8. Refresh the page (make sure to keep the developer tools open).

##### Windows:
  1. Open the Developer Tools in Chrome (Ctrl + shift + i)
  2. Open settings (F1)
  3. Click "Devices" on the left side of the settings panel.
  4. Click "Add custom device..."
  6. Choose a name (eg. Googlebot), screen size and enter the following User agent string: 
	   ```
       Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)
	   ```
  7. Make sure the new device is checked.
  8. You can now choose it from the device dropdown in the Developer Tools screen.
