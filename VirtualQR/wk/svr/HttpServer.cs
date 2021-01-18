using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Threading;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;


namespace wk.svr
{
    public class HttpServer : IDisposable
    {
        private TcpListener m_svr;
        private bool bStop;
        private bool bStopped;
        private Thread curThread;
        public st_clientSet[] arrClient;
        public delegate void FpExit();
        public FpExit cbExit;

        public HttpServer()
        {

        }

        public bool isStopped()
        {
            return bStopped;
        }

        private void tcpProc()
        {
            Exception exInit = null;
            TcpListener srv = new TcpListener(IPAddress.Any, 8888);
            m_svr = srv;

            try
            {
                srv.Start();
            }
            catch (Exception exSck)
            {
                logEx(exSck);
                forceExit("Cannot open server");
                return;
            }

            try
            {
                loadExcelTable();
            }
            catch (Exception exExcel)
            {
                logEx(exExcel);
                exInit = exExcel;

                string msg = String.Format("Cannot Open {0}", f);
                forceExit(msg);
                return;
            }

            try
            {
                if (null != exInit)
                {
                    throw exInit;
                }

                while (!bStop)
                {
                    try
                    {
                        TcpClient tc = srv.AcceptTcpClient();
                        processRequest(tc, ref arrClient[0]);
                    }
                    catch (Exception exLoop)
                    {
                        logEx(exLoop);
                    }
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().ToString());
            }
            bStopped = true;
        }

        private const byte CR = (byte) '\r';
        private const byte LF = (byte) '\n';
        private const byte SP = (byte)' ';
        private const byte DIV = (byte)'/';
        private const byte QM = (byte) '?';
        private const int MIN_QUERY = 11;             // min query is /
        private const int MIN_FILE = MIN_QUERY + 2;   // min file path is /f/
        private const int SZ_IMG_BUF = 4000;

        private bool match(byte[] p, int iStart, string s)
        {
            int iEnd = iStart + s.Length;
            for (int i = iStart, k = 0; i < iEnd; i++, k++)
            {
                if (s[k] != p[i]) return false;
            }
            return true;
        }


        private const int MIME_TEXT = 0;
        private const int MIME_HTML = 1;
        private const int MIME_JS = 2;
        private const int MIME_PNG = 3;
        private const int MIME_ICO = 4;
        private const int MIME_BINARY = 5;
        private const string fmtBody = "<h1>{0} - {1}</h1>";
        private const string fmtHead = "HTTP/1.1 {0} {1}\r\nContent-Type: text/html\r\nConnection: close\r\n\r\n";
        private const string fmtHead2 = "HTTP/1.1 {0} {1}\r\nLocation: {2}\r\nContent-Type: text/html\r\nConnection: close\r\n\r\n";
        private struct OkBuf
        {
            public byte[] val;
        }
        private struct ErrBuf
        {
            public byte[] head;
            public byte[] body;
        }
        private static OkBuf[] buildOkMine()
        {
            string fmt = "HTTP/1.1 200 OK\r\nContent-Type: {0}\r\nConnection: close\r\n\r\n";
            string[] mimeArr = new string[] {
                "text/plain",
                "text/html",
                "application/javascript",
                "image/png",
                "image/x-icon",
                "application/octet-stream"
            };
            OkBuf[] okArr = new OkBuf[mimeArr.Length];
            for (int i = 0; i < mimeArr.Length; i++)
            {
                string str = String.Format(fmt, mimeArr[i]);
                okArr[i].val = Encoding.UTF8.GetBytes(str);
            }
            return okArr;
        }
        private readonly ErrBuf[] errArr = new ErrBuf[1000];
        private readonly OkBuf[] okArr = buildOkMine();
        private Dictionary<int, string> mapHttpErr = buildErrMap();
        private static Dictionary<int, string> buildErrMap()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[307] = "Temporary Redirect";
            d[308] = "Permanent Redirect";
            d[404] = "Not Found";
            d[501] = "Not Implemented";
            return d;
        }
        private bool serveErr(NetworkStream ns, int err)
        {
            if (err < 1000)
            {
                byte[] hd = errArr[err].head;
                byte[] bd;
                if (null == hd)
                {
                    string msg;
                    if (mapHttpErr.ContainsKey(err))
                    {
                        msg = mapHttpErr[err];
                    }
                    else
                    {
                        // "Not Implemented";
                        return serveErr(ns, 501);
                    }

                    bd = Encoding.UTF8.GetBytes(String.Format(fmtBody, err, msg));
                    if (307 == err)
                    {
                        hd = Encoding.UTF8.GetBytes(String.Format(fmtHead2, err, msg, "/f/index.html"));
                    }
                    else
                        hd = Encoding.UTF8.GetBytes(String.Format(fmtHead, err, msg));

                    ErrBuf eb;
                    eb.head = hd;
                    eb.body = bd;
                    errArr[err] = eb;
                }
                else
                {
                    bd = errArr[err].body;
                }
                ns.Write(hd, 0, hd.Length);
                ns.Write(bd, 0, bd.Length);
            }
            return false;
        }



        private void processRequest(TcpClient tc, ref st_clientSet rc)
        {
            NetworkStream ns = tc.GetStream();
            rc.m_tcp = tc;
            rc.m_ns = ns;
            ns.ReadTimeout = ns.WriteTimeout = 3000;

            byte[] buf = rc.m_buf;
            int nRead;
            try
            {
                nRead = ns.Read(buf, 0, st_clientSet.sz_buf);
            }
            catch (Exception)
            {
                Console.WriteLine("Read Timeout");
                ns.Close();
                tc.Close();
                return;
            }
             
            int i1 = Array.IndexOf<byte>(buf, SP, 0, nRead) + 1;
            int i2 = Array.IndexOf<byte>(buf, LF, i1, nRead - i1); // check 1st LF
            int iQuest = -1;
            int len = 0;
            int nHttpVer = -1;

            bool bVerOk = false;
            bool bHeadEndOk = false;
            bool bServed = false;

            if (i1 > 0 && i2 > i1)
            {
                len = (i2 - i1);
                if (len >= MIN_QUERY)
                {
                    byte chk;
                    chk = buf[i2 - 1];
                    if (CR == chk) // check 1st CR
                    {
                        chk = buf[i2 - 2]; // check http 1.0 or 1.1
                        if (chk == '0' || chk == '1') { nHttpVer = (chk - '0'); }
                    }
                    i2 -= 10;
                    bVerOk = (nHttpVer >= 0) && match(buf, i2, " HTTP/1.");
                }

                bHeadEndOk = match(buf, nRead - 4, "\r\n\r\n");
                //Console.WriteLine("bVerOk ?     {0}", bVerOk);
                //Console.WriteLine("bHeadEndOk ? {0}", bHeadEndOk);

                iQuest = Array.IndexOf<byte>(buf, QM, i1, i2 - i1);
                if (iQuest < 0) iQuest = i2;
            }
            if (bVerOk && bHeadEndOk)
            {
                int c = (int) buf[0];
                if ('G' == c && match(buf, 0, "GET "))
                {
                    string qstr = "";
                    /*
                    byte[] res = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: {0}\r\nConnection: close\r\n\r\n+AB");
                    //ns.Write(res, 0, res.Length);

                    */
                    string pth = Encoding.UTF8.GetString(buf, i1, iQuest - i1);
                    Console.WriteLine("uri {0}", Encoding.UTF8.GetString(buf, i1, iQuest - i1));
                    Console.WriteLine(" - query {0}", Encoding.UTF8.GetString(buf, iQuest, i2 - iQuest));

                    char qType = ' ';
                    if (len >= MIN_FILE)
                    {
                        if (DIV == buf[i1] && DIV == buf[i1 + 2])
                        {
                            qType = (char) buf[i1 + 1];
                        }
                    }

                    if ('f' == qType)
                    {
                        i1++;
                        string path = Encoding.UTF8.GetString(buf, i1, iQuest - i1);
                        bServed = serveAnyFile(ns, path);
                    }

                    if ('q' == qType)
                    {
                        i1 += 3;
                        try
                        {
                            string qr = Encoding.UTF8.GetString(buf, i1, i2 - i1);
                            if (qr.IndexOf('%') >= 0)
                            {
                                try
                                {
                                    Console.WriteLine("# QR: {0}", qr);
                                    string decoded = Uri.UnescapeDataString(qr);
                                    Console.WriteLine(" - decoded QR: {0}", decoded);
                                    qr = (decoded);
                                }
                                catch (Exception) { }
                            }

                            streamQr(qr, ns);
                            bServed = true;
                        }
                        catch (Exception ex)
                        {
                            logEx(ex);
                        }
                    }

                    if ('l' == qType)
                    {
                        i1 += 3;
                        string order = Encoding.UTF8.GetString(buf, i1, iQuest - i1);
                        if ("head/".Equals(order))
                        {
                            askHead(ns);
                            bServed = true;
                        }
                        else
                        {
                            // cut date param
                            qstr = Encoding.UTF8.GetString(buf, iQuest, i2 - iQuest);
                            qstr = cutParam(qstr, "?date=");
                            // ask order
                            askOrder(order, ns, qstr);
                            bServed = true;
                        }
                    }

                    if (!bServed)
                    {
                        string unknown = Encoding.UTF8.GetString(buf, i1, iQuest - i1);
                        if ("/favicon.ico".Equals(unknown))
                        {
                            bServed = serveAnyFile(ns, "f/favicon.ico");
                        }
                        else if ("/".Equals(unknown))
                        {
                            serveErr(ns, 307);
                            bServed = true;
                        }
                    }
                } // end - http GET
            }

            if (!bServed)
            {
                serveErr(ns, 404);
            }
            ns.Flush();

            ns.Close();
            tc.Close();
        }
        private string cutParam(string qstr, string pattern)
        {
            if (qstr.StartsWith(pattern))
            {
                return qstr.Substring(pattern.Length);
            }
            return "";
        }

        private static void logEx(Exception ex)
        {
            Console.WriteLine("Exception: {0}", ex.GetType().ToString());
            Console.WriteLine(" - msg: {0}", ex.Message);
            Console.WriteLine(" - trace: {0}", ex.StackTrace);
        }
        private static void printC(byte[] b, int idx)
        {
            int c = b[idx];
            Console.WriteLine("0x{0:X4}   {0:D4}  {1}", c, (char)c);
        }
        private bool serveAnyFile(NetworkStream ns, string f)
        {
            StringComparison cp = StringComparison.OrdinalIgnoreCase;
            if (f.EndsWith(".png", cp))
            {
                return serveFile(MIME_PNG, ns, f);
            }
            else if (f.EndsWith(".html", cp))
            {
                return serveFile(MIME_HTML, ns, f);
            }
            else if (f.EndsWith(".js", cp))
            {
                return serveFile(MIME_JS, ns, f);
            }
            else if(f.EndsWith(".ico", cp))
            {
                return serveFile(MIME_ICO, ns, f);
            }

            return serveFile(MIME_BINARY, ns, f);
            //return false;
        }
        private bool serveFile(int nMime, NetworkStream ns, string f)
        {
            if (File.Exists(f))
            {
                byte[] ok = okArr[nMime].val;
                byte[] data = System.IO.File.ReadAllBytes(f);
                ns.Write(ok, 0, ok.Length);
                ns.Write(data, 0, data.Length);
                return true;
            }
            return false;
        }
        private void askHead(Stream ns)
        {
            byte[] ok = okArr[MIME_TEXT].val;
            ns.Write(ok, 0, ok.Length);

            Excel.Workbook wb = m_wb;
            if (null == wb)
            {
                loadExcelTable();
                wb = m_wb;
            }
            if (null == wb) return;
            using (StreamWriter wr = new StreamWriter(ns, Encoding.UTF8))
            {
                int nTotalCol = 0;
                if (wb.Sheets.Count > 0)
                {
                    Excel.Worksheet sh = wb.Sheets[1];
                    Excel.Range used = sh.UsedRange;
                    nTotalCol = used.Columns.Count;

                    int i = 1;
                    object v1 = "";
                    object v2 = "";
                    object v3 = "";
                    object vEx01 = "";
                    object vEx02 = "";
                    object vEx03 = "";
                    if (nTotalCol >= 1) v1 = nonNull(sh.Cells[i, 1].Value2);
                    if (nTotalCol >= 2) v2 = nonNull(sh.Cells[i, 2].Value2);
                    if (nTotalCol >= 3) v3 = nonNull(sh.Cells[i, 3].Value2);
                    if (nTotalCol >= 5) vEx01 = nonNull(sh.Cells[i, 5].Value2);
                    if (nTotalCol >= 6) vEx02 = nonNull(sh.Cells[i, 6].Value2);
                    if (nTotalCol >= 7) vEx03 = nonNull(sh.Cells[i, 7].Value2);
                    xWrite(wr, true, v1, v2, v3, vEx01, vEx02, vEx03, "Date", nTotalCol);
                }

                wr.Flush();
            }
        }
        private void askOrder(string s, Stream ns, string date)
        {
            Console.WriteLine("order: [{0}]", s);
            Console.WriteLine("date: [{0}]", date);
            bool bFilterDate = (date.Length > 0);
            DateTime dtFilter = DateTime.FromBinary(0);
            if (bFilterDate)
            {
                dtFilter = DateTime.Parse(date);
            }

            Excel.Workbook wb = m_wb;
            if (null == wb)
            {
                loadExcelTable();
                wb = m_wb;
            }
            if (null != wb)
            {
                if (wb.Sheets.Count > 0)
                {
                    Excel.Worksheet sh = wb.Sheets[1];

                    Excel.Range used = sh.UsedRange;
                    int nLast = used.Rows.Count;
                    int nTotalCol = used.Columns.Count;
                    if (nTotalCol >= 4)
                    {
                        byte[] ok = okArr[MIME_TEXT].val;
                        ns.Write(ok, 0, ok.Length);
                        using (MemoryStream ms = new MemoryStream(SZ_IMG_BUF))
                        {
                            int pos = 0;
                            int nAccu = 0;
                            using (StreamWriter wr = new StreamWriter(ms, Encoding.UTF8))
                            {
                                wr.WriteLine("[");
                                for (int i = 2; i <= nLast; i++)
                                {
                                    Excel.Range c1 = sh.Cells[i, 1];
                                    Excel.Range c2 = sh.Cells[i, 2];
                                    Excel.Range c3 = sh.Cells[i, 3];
                                    Excel.Range c4 = sh.Cells[i, 4];
                                    object v1 = nonNull(c1.Value2);
                                    object v2 = nonNull(c2.Value2);
                                    object v3 = nonNull(c3.Value2);
                                    object v4 = nonNull(c4.Value2);
                                    // extra param
                                    object vEx01 = "";
                                    object vEx02 = "";
                                    object vEx03 = "";
                                    if (nTotalCol >= 5) vEx01 = nonNull(sh.Cells[i, 5].Value2);
                                    if (nTotalCol >= 6) vEx02 = nonNull(sh.Cells[i, 6].Value2);
                                    if (nTotalCol >= 7) vEx03 = nonNull(sh.Cells[i, 7].Value2);
                                    string strDuty = "";

                                    double dDate = Convert.ToDouble(v4);
                                    DateTime dtDate = DateTime.FromOADate(dDate);
                                    if (bFilterDate)
                                    {
                                        if (dtFilter != dtDate) continue;
                                    }
                                    strDuty = dtDate.ToString("yyyy-MM-dd");

                                    if (!v3.Equals(s))
                                    {
                                        Console.WriteLine("s..{0}", s);
                                        Console.WriteLine(" - v1..{0}", v1);
                                        Console.WriteLine(" - v2..{0}", v2);
                                        Console.WriteLine(" - v3..{0}", v3);
                                        Console.WriteLine(" - v4..{0}", v4);

                                        Console.WriteLine(" - tp..{0}", v3.GetType());
                                        continue;
                                    }

                                    //xWrite(wr, (0 == nAccu), v1, v2, v3, "", "", "", strDuty);
                                    xWrite(wr, (0 == nAccu), v1, v2, v3, vEx01, vEx02, vEx03, strDuty);
                                    nAccu++;
                                    //Console.WriteLine("# listing {0},{1},{2}", v1, v2, v3);
                                }
                                wr.WriteLine("]");
                                wr.Flush();
                                ms.Flush();
                                pos = (int)ms.Position;
                                ms.Seek(0, SeekOrigin.Begin);
                                ns.Write(ms.GetBuffer(), 0, pos);
                            }
                            ns.Flush();
                        }
                    }
                }
            }
        }
        private string[] sArr = { "Part", "Batch", "Operator", "Extra_01", "Extra_02", "Extra_03", "Duty", "Count" };
        private void xWrite(StreamWriter wr, bool bFirst, params object[] pArr)
        {
            int len = pArr.Length;
            len = Math.Min(len, sArr.Length);
            wr.WriteLine(bFirst ? "{" : ",\r\n{");
            //wr.WriteLine("{");
            for (int i = 0; i < len; i++)
            {
                wr.Write("\"{0}\": \"{1}\"", sArr[i], pArr[i]);
                wr.WriteLine(i < (len-1) ? "," : "");
            }
            wr.WriteLine("}");
        }
        private object nonNull(object o)
        {
            return (null == o) ? "" : o.ToString(); 
        }

        private static st_clientSet[] prepareClient()
        {
            int sz = st_clientSet.sz_client;
            st_clientSet[] arr = new st_clientSet[sz];
            for (int i = 0; i < sz; i++)
            {
                arr[i] = st_clientSet.makeOne();
            }
            return arr;
        }

        public void start()
        {
            if (null == curThread)
            {
                arrClient = prepareClient();

                Thread t = new Thread(tcpProc);
                curThread = t;
                t.Start();
            }
        }

        public void stop()
        {
            bStop = true;
            closeExcel();
            TcpListener svr = m_svr;
            if(null != svr) svr.Stop();
        }

        public void Dispose()
        {
            stop();
        }

        public static Bitmap makeQr(string data)
        {
            ZXing.QrCode.QrCodeEncodingOptions op = new ZXing.QrCode.QrCodeEncodingOptions();
            ZXing.BarcodeWriter bar = new ZXing.BarcodeWriter();
            bar.Format = ZXing.BarcodeFormat.QR_CODE;
            bar.Options = op;
            op.Hints.Add(ZXing.EncodeHintType.CHARACTER_SET, "utf-8");

            int szImg = 160;
            bar.Options.Width = szImg;
            bar.Options.Height = szImg;
            Bitmap bm = bar.Write(data);
            return bm;
        }

        private void streamQr(string data, NetworkStream ns)
        {
            using (Bitmap bm = makeQr(data))
            {
                using (MemoryStream ms = new MemoryStream(SZ_IMG_BUF))
                {
                    bm.Save(ms, ImageFormat.Png);
                    int szByte = (int)ms.Position;
                    ms.Position = 0;

                    byte[] msBuf = ms.GetBuffer();
                    byte[] ok = okArr[MIME_PNG].val;
                    ns.Write(ok, 0, ok.Length);
                    ns.Write(msBuf, 0, szByte);
                }
            }
        }


        private string f = @"order.xlsx";
        private Excel.Application m_app;
        private Excel.Workbook m_wb;
        public void loadExcelTable()
        {
            string bs = Assembly.GetAssembly(typeof(HttpServer)).Location;
            int idx = bs.LastIndexOf('\\');
            if (idx > 0)
            {
                bs = bs.Substring(0, 1 + idx);
            }

            string fullPath = bs + f;
            if (!File.Exists(fullPath))
            {
                try
                {
                    byte[] all = File.ReadAllBytes(bs + "f\\_sample.xlsx");
                    File.WriteAllBytes(fullPath, all);
                }
                catch (Exception) { }
            }

            // ### use Editable:true is more stable than File.OpenWrite() ###
            // #old# ensure write access
            // #old# FileStream fs = File.OpenWrite(fullPath);
            {
                // #old# fs.Dispose();

                Excel.Application xApp = new Excel.Application();
                Excel.Workbook xWb = xApp.Workbooks.Open(fullPath, 0, false, Editable:true);
                xApp.Visible = false;

                m_app = xApp;
                m_wb = xWb;
            }
        }
        private void closeExcel()
        {
            releaseComObj(m_wb, fpCloseWorkbook);
            releaseComObj(m_app, fpCloseExcel);
        }
        private void releaseComObj(object o, params FpClose[] arrFp)
        {
            try
            {
                if (null != o)
                {
                    foreach (FpClose fp in arrFp)
                    {
                        fp(o);
                    }
                    Marshal.ReleaseComObject(o);
                    Marshal.FinalReleaseComObject(o);
                }
            }
            catch (Exception) { }
        }

        private delegate void FpClose(object o);
        private void fpCloseWorkbook(object o)
        {
            Excel.Workbook xWb = o as Excel.Workbook;
            if (null != xWb)
            {
                try
                {
                    xWb.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.GetType().ToString());
                }
                m_wb = null;
            }
        }
        private void fpCloseExcel(object o)
        {
            Excel.Application xApp = o as Excel.Application;
            if (null != xApp)
            {
                try
                {
                    xApp.Quit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.GetType().ToString());
                }
                m_app = null;
            }
        }

        private void forceExit(string msg)
        {
            MessageBox.Show(msg);
            try
            {
                stop();
            }
            catch (Exception) { }
            cbExit();
        }

    } // end - class HttpServer
}
