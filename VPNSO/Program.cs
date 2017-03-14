using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VPNSO
{
    class ProxyInfo
    {
        public ProxyInfo()
        {
            Tims = new List<long>();
        }

        public string Name
        {
            get; set;
        }
        public string Ip { get; set; }

        public int Port { get; set; }

        public string UserName
        {
            get; set;
        }
        public string Password
        {
            get; set;
        }

        public string RemoteIp { get; set; }

        public int RemotePort { get; set; }
        public List<long> Tims { get; set; } 
    }

    class Program
    {
        static string serverListURL = "https://sslauth.60in.com/RPC/index.php?action=serverlistjson";
        private static readonly List<ProxyInfo> proxyList = new List<ProxyInfo>();
        static void Main(string[] args)
        {
            GetProxyServerList();
            TestProxyServer();
            Console.ReadLine();
        }

        static void TestProxyServer()
        {
            foreach (var proxyInfo in proxyList)
            {
                HTTP_GET_WITHPROXY(proxyInfo);
            }

            Console.WriteLine("》》》推荐：");

            var good = proxyList.OrderBy(p => p.Tims.Skip(1).Sum()).Take(3);
            foreach (var proxyInfo in good)
            {
                Console.WriteLine(proxyInfo.Name);
                Console.WriteLine(proxyInfo.Ip + ":" + proxyInfo.Port);
                Console.WriteLine(string.Join(Environment.NewLine, proxyInfo.Tims.Select(t => "-timing:" + t)));
                Console.WriteLine("*******************************************************************");
            }
            Console.WriteLine("》》》测试完成，任意键退出");
            Console.ReadLine();
        }

        static void GetProxyServerList()
        {
            Console.WriteLine("》》》正在获取远程代理服务列表……");
            HttpStatusCode stateCode;
            var listStr = Get(serverListURL, out stateCode);
            if (stateCode == HttpStatusCode.OK)
            {
                //Console.WriteLine(listStr);
                //Console.WriteLine();
                listStr = listStr.Trim('(', ')');
                var list = JsonConvert.DeserializeObject<Dictionary<string, string>>(listStr);

                var i = 9900;
                foreach (var keyValuePair in list)
                {
                    var lowerKey = keyValuePair.Key.ToLower();
                    if (!(lowerKey.StartsWith("jp") || lowerKey.StartsWith("s") || lowerKey.StartsWith("kr") || lowerKey.StartsWith("de")))
                    {
                        continue;
                    }
                    var proxyInfo = new ProxyInfo
                    {
                        Name = i + "-" + keyValuePair.Value,
                        Ip = GetLocalIpv4().FirstOrDefault(),
                        Port = i,
                        RemoteIp = keyValuePair.Key.Trim(),
                        RemotePort = 443,
                        UserName = ConfigurationManager.AppSettings["UserName"],
                        Password = ConfigurationManager.AppSettings["Password"],
                    };
                    proxyList.Add(proxyInfo);

                    Console.WriteLine("[" + proxyInfo.Name + "]");
                    Console.WriteLine("client = yes");
                    Console.WriteLine("accept = " + proxyInfo.Ip + ":" + proxyInfo.Port);
                    Console.WriteLine("connect = " + proxyInfo.RemoteIp + ":" + proxyInfo.RemotePort);
                    Console.WriteLine();
                    i++;
                }

            }
            Console.WriteLine("》》》代理服务列表获取完成,启动Stunnel客户端后，任意键测试速度");
            Console.ReadLine();
        }

        static void HTTP_GET_WITHPROXY(ProxyInfo proxy, string TARGETURL = "https://www.google.com/", int testCount = 5)
        {
            Console.WriteLine(proxy.Name.Split('-')[1]);
            Console.WriteLine("Proxy:" + proxy.Ip + ":" + proxy.Port);
            Console.WriteLine("GET:" + TARGETURL);
            HttpClientHandler handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxy.Ip, proxy.Port) { Credentials = new NetworkCredential(proxy.UserName, proxy.Password) },
                PreAuthenticate = true,
                UseProxy = true
            };
            HttpClient client = new HttpClient(handler) { Timeout = new TimeSpan(0, 0, 4) };

            for (int i = 1; i <= testCount; i++)
            {
                Ping(proxy, TARGETURL, client, i);
            }

            Console.WriteLine("----------------------------------------------------------------");
        }

        private static void Ping(ProxyInfo proxy, string TARGETURL, HttpClient client, int times)
        {
            int ERRORTIME = 1000 * 60;
            if (proxy.Tims.Contains(ERRORTIME))
            {
                proxy.Tims.Add(ERRORTIME);
                Console.WriteLine("-Timing: x:ERROR");
                return;
            }

            try
            {
                var timer = new Stopwatch();
                timer.Start();
                HttpResponseMessage response = client.GetAsync(TARGETURL).Result;
                HttpContent content = response.Content;
                if (response.StatusCode == HttpStatusCode.GatewayTimeout)
                {
                    proxy.Tims.Add(ERRORTIME);
                    Console.WriteLine("-Timing: x:GatewayTimeout > 4000ms");
                    return;
                }
                if (response.StatusCode == HttpStatusCode.RequestTimeout)
                {
                    proxy.Tims.Add(ERRORTIME);
                    Console.WriteLine("-Timing: x:RequestTimeout > 4000ms");
                    return;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    proxy.Tims.Add(ERRORTIME);
                    Console.WriteLine("-Timing: x:" + response.StatusCode);
                    return;
                }
                timer.Stop();
                string result = content.ReadAsStringAsync().Result;
                var executionTime = timer.ElapsedMilliseconds;
                proxy.Tims.Add(executionTime);
                Console.WriteLine("-Timing: " + executionTime + "ms");
            }
            catch (Exception)
            {
                proxy.Tims.Add(ERRORTIME);
                Console.WriteLine("-Timing: x:ERROR");
            }
        }

        static string[] GetLocalIpv4()
        {
            //事先不知道ip的个数，数组长度未知，因此用StringCollection储存
            IPAddress[] localIPs;
            localIPs = Dns.GetHostAddresses(Dns.GetHostName());
            StringCollection IpCollection = new StringCollection();
            foreach (IPAddress ip in localIPs)
            {
                //根据AddressFamily判断是否为ipv4,如果是InterNetWorkV6则为ipv6
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    IpCollection.Add(ip.ToString());
            }
            string[] IpArray = new string[IpCollection.Count];
            IpCollection.CopyTo(IpArray, 0);
            return IpArray;
        }

        private static string Get(string url, out HttpStatusCode stateCode)
        {
            try
            {
                using (var client = new HttpClient())
                {
                  //var a=  client.GetAsync()
                  //  client.Timeout = new TimeSpan(0, 0, 5);
                    var response = client.GetAsync(url).Result;
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    stateCode = response.StatusCode;
                    return responseString;
                }
            }
            catch (Exception e)
            {
                stateCode = HttpStatusCode.BadRequest;
                return "[]";
            }
        }
    }
}
