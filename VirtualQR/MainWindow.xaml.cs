using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Threading;
using wk.svr;

namespace VirtualQR
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        HttpServer svr = new HttpServer();
        List<string> lstExclude = new List<string>();
        DispatcherTimer tmRefresh = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(150) };

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            tmRefresh.Tick += TmRefresh_Tick;
            lstExclude.Add("169.254.*");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Title += String.Format("  {0}", versionString());
            svr.cbExit = this.closeWnd;
            svr.start();
            scanNetInterface();
        }

        private void TmRefresh_Tick(object sender, EventArgs e)
        {
            tmRefresh.Stop();
            scanNetInterface();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            svr.stop();
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                xWrap.Children.Clear();
                tmRefresh.Stop();
                tmRefresh.Start();
                e.Handled = true;
            }
        }


        private void closeWnd()
        {
            Action act = this.Close;
            Dispatcher.Invoke(act);
        }

        private bool tryIfExclude(string rule, string str)
        {
            if (rule.EndsWith(".*"))
            {
                rule = rule.Substring(0, rule.Length - 1);
                bool bIsExclude = str.StartsWith(rule);
                return bIsExclude;
            }
            return rule.Equals(str);
        }

        private void scanNetInterface()
        {
            bool bScanIpv6 = (true == cbEnableIpv6.IsChecked);
            xWrap.Children.Clear();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                string nm = nic.Name;
                OperationalStatus stat = nic.OperationalStatus;
                NetworkInterfaceType nicType = nic.NetworkInterfaceType;

                bool bIsE1 = stat.HasFlag(OperationalStatus.Up);
                bool bIsE2 = (nicType.HasFlag(NetworkInterfaceType.Ethernet));
                bIsE2 = bIsE2 && (!nic.NetworkInterfaceType.HasFlag(NetworkInterfaceType.Wireless80211));
                //bool bIsEnable = (bIsE1 || bIsE2);
                bool bIsEnable = (bIsE1);

                if (!bIsEnable) continue;
                if (nicType.HasFlag(NetworkInterfaceType.Loopback)) continue;


                foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                {
                    string strIp = ip.Address.ToString();
                    bool bIsIpv6 = (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
                    if (bIsIpv6)
                    {
                        if (!bScanIpv6) continue;
                        if (strIp.Contains('%'))
                        {
                            strIp = strIp.Substring(0, strIp.IndexOf('%'));
                        }
                    }
                    bool bExclude = false;
                    foreach (string rule in lstExclude)
                    {
                        if (tryIfExclude(rule, strIp))
                        {
                            bExclude = true;
                            break;
                        }
                    }
                    if (!bExclude)
                    {
                        Button btn = new Button();
                        btn.Click += Btn_Click;

                        st_MyNic m;
                        m.m_name = nic.Name;
                        m.m_addr = strIp;
                        m.m_imgCache = null;

                        btn.Content = m;
                        xWrap.Children.Add(btn);
                    }
                }      
            } // end - for
        } // end - scanNetInterface()


        public struct st_MyNic
        {
            public string m_name;
            public string m_addr;
            public string Name { get { return m_name; } }
            public string Addr { get { return m_addr; } }
            public BitmapImage m_imgCache;

            public string DefaultUrl
            {
                get
                {
                    string ip = m_addr;
                    string url = String.Format("http://{0}:8888/f/index.html", ip);
                    return url;
                }
            }
            public BitmapImage QrImage
            {
                get
                {
                    BitmapImage bImgOrig = m_imgCache;
                    if (null != bImgOrig)
                    {
                        return bImgOrig;
                    }

                    string url = DefaultUrl;
                    Bitmap bm = HttpServer.makeQr(url);
                    BitmapImage bImg = transferTo(ref bm);
                    m_imgCache = bImg;
                    return bImg;
                }
            }

            private static BitmapImage transferTo(ref Bitmap bm)
            {
                using (System.IO.MemoryStream memory = new System.IO.MemoryStream())
                {
                    bm.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                    memory.Position = 0;
                    BitmapImage bitmapimage = new BitmapImage();
                    bitmapimage.BeginInit();
                    bitmapimage.StreamSource = memory;
                    bitmapimage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapimage.EndInit();

                    bm.Dispose();
                    bm = null;
                    return bitmapimage;
                }
            }
        }

        private void Btn_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            st_MyNic nic = (st_MyNic)btn.Content;

            string ip = nic.Addr;
            string url = nic.DefaultUrl;
            Process.Start(url);
        }

        private void cbEnableIpv6_Click(object sender, RoutedEventArgs e)
        {
            scanNetInterface();
        }

        public string versionString()
        {
            string fullVer;
            Version ver = typeof(App).Assembly.GetName().Version;
            int v1 = ver.Major;
            int v2 = ver.Minor;
            int v3 = ver.Build;
            int v4 = ver.Revision;

            if (v4 > 0)
            {
                string strSpecial = "alpha";
                int nStartBeta = 1000;
                if (v4 > nStartBeta)
                {
                    strSpecial = "beta";
                    v4 -= nStartBeta;
                }
                fullVer = String.Format(App.APP_SPECIAL, v1, v2, v3, v4, strSpecial);
            }
            else
            {
                fullVer = String.Format(App.APP_NORMAL, v1, v2, v3);
            }
            return fullVer;
        }

    } // end - class MainWindow
}
