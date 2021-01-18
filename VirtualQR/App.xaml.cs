using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace VirtualQR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // version define
        public const string AssemblyFileVersion = "1.0.0.0";

        // version display
        // - fullVer = String.Format(App.APP_NORMAL, v1, v2, v3);
        public const string APP_NORMAL = "{0}.{1}.{2}"; // formal release
        // - fullVer = String.Format(App.APP_SPECIAL, v1, v2, v3, v4, strSpecial);
        public const string APP_SPECIAL = "{0}.{1}.{2} {4} {3:D2}"; // alpha or beta
    }
}
