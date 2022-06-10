﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

namespace SteamCMD
{
    internal static class UpdateService
    {
        private const string Version = "0.0.4v0";
        private const string Uri = "https://github.com/serser007/steamcmdtool/releases/latest";
        public static string GetVersion()
        {
            var uri = WebRequest.Create(Uri).GetResponse().ResponseUri;
            return uri.AbsoluteUri.Split('/').Last();
        } 

        public static bool CheckForUpdates()
        {
             return Version != GetVersion();
        }

        public static void OpenURI()
        {
            try { 
                Process.Start($"cmd", $"/min /q /c start {Uri}");
            }
            catch
            {
            }
        }
    }
}
