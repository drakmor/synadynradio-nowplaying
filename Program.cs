using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Timers;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;

namespace digiton_nowplaying
{
    class Program
    {
        private static string currentSong = "";
        private static System.Timers.Timer timer;
        private static string songsFilePath;
        private static string filePrefix;
        private static string serverIP;
        private static int serverPort;
        private static NotifyIcon notifyIcon;

        [STAThread]
        static void Main(string[] args)
        {
            LoadSettings();
            StartTrayApplication();
            StartWebServer();
            StartTimer();
            Application.Run(); // Запуск цикла сообщений на основном потоке
        }

        private static void LoadSettings()
        {
            try
            {
                if (!File.Exists("settings.ini"))
                {
                    MessageBox.Show("Файл settings.ini не найден.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                }

                foreach (var line in File.ReadAllLines("settings.ini"))
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        if (key.Equals("SongsFilePath", StringComparison.OrdinalIgnoreCase))
                        {
                            songsFilePath = value;
                        }
                        else if (key.Equals("FilePrefix", StringComparison.OrdinalIgnoreCase))
                        {
                            filePrefix = value;
                        }
                        else if (key.Equals("ServerIP", StringComparison.OrdinalIgnoreCase))
                        {
                            serverIP = value;
                        }
                        else if (key.Equals("ServerPort", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(value, out serverPort))
                            {
                                MessageBox.Show("Некорректный формат порта в settings.ini.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                Environment.Exit(1);
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(songsFilePath) || string.IsNullOrEmpty(filePrefix) || string.IsNullOrEmpty(serverIP) || serverPort == 0)
                {
                    MessageBox.Show("Некорректные настройки в settings.ini.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                }

                // Проверка корректности IP-адреса
                if (!IPAddress.TryParse(serverIP, out _))
                {
                    MessageBox.Show("Некорректный IP-адрес в settings.ini.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке настроек: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }

        private static void StartTrayApplication()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);


                notifyIcon = new NotifyIcon
                {
                    Icon = Properties.Resources.TrayIcon,
                    Visible = true,
                    Text = "Synadyn Radio Now Playing"
                };

                var contextMenu = new ContextMenuStrip();

                string serverUrl = $"http://{serverIP}:{serverPort}/";
                contextMenu.Items.Add($"Оригинальное название {serverUrl}", null, (s, e) => OpenWebServerUrl(serverUrl));
                contextMenu.Items.Add($"Транслитерация {serverUrl}translit/", null, (s, e) => OpenWebServerUrl(serverUrl+"translit/"));
                contextMenu.Items.Add($"RadioText {serverUrl}rt/", null, (s, e) => OpenWebServerUrl(serverUrl+"rt/"));
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Выход", null, OnExit);
                notifyIcon.ContextMenuStrip = contextMenu;

                // Tooltip для отображения текущей песни
                notifyIcon.MouseMove += (s, e) =>
                {
                    notifyIcon.Text = GetTrimmedTooltip("Играет: " + currentSong);
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при инициализации трея: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }

        private static void OnExit(object sender, EventArgs e)
        {
            try
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                Application.Exit();
            }
            catch (Exception ex)
            {
                // Логирование ошибки при выходе
            }
        }

        private static void StartTimer()
        {
            timer = new System.Timers.Timer(1000); // 1 секунда
            timer.Elapsed += (s, e) => UpdateCurrentSong();
            timer.AutoReset = true;
            timer.Start();
        }

        private static void UpdateCurrentSong()
        {
            if (File.Exists(songsFilePath))
            {
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    using (StreamReader reader = new StreamReader(songsFilePath, Encoding.GetEncoding("windows-1251")))
                    {
                        xmlDoc.Load(reader);
                    }
                    XmlNodeList broadcastItems = xmlDoc.GetElementsByTagName("BroadcastItem");
                    foreach (XmlNode item in broadcastItems)
                    {
                        var filePathNode = item["Digiton.FilePath"];
                        var artistTitleNode = item["ArtistTitle"];

                        if (filePathNode != null && artistTitleNode != null)
                        {
                            string filePath = filePathNode.InnerText;
                            if (filePath.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                string artistTitle = artistTitleNode.InnerText.Trim();
                                if (!string.IsNullOrEmpty(artistTitle))
                                {
                                    // Удаление скобок и их содержимого при парсинге
                                    artistTitle = RemoveParenthesesContent(artistTitle);
                                    currentSong = artistTitle; break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Логирование или обработка исключения
                }
            }
        }

        private static void StartWebServer()
        {
            string basePrefix = $"http://{serverIP}:{serverPort}/";

            try
            {
                var server = new HttpListener();

                // Формируем префиксы из конфигурации
                server.Prefixes.Add($"{basePrefix}");
                server.Prefixes.Add($"{basePrefix}artist/");
                server.Prefixes.Add($"{basePrefix}title/");
                server.Prefixes.Add($"{basePrefix}translit/");
                server.Prefixes.Add($"{basePrefix}rt/");


                server.Start();

                ThreadPool.QueueUserWorkItem(o =>
                {
                    try
                    {
                        while (server.IsListening)
                        {
                            var context = server.GetContext();
                            ThreadPool.QueueUserWorkItem((c) => HandleRequest(c), context);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Логирование исключения
                    }
                });
            }
            catch (HttpListenerException hlex)
            {
                MessageBox.Show($"Ошибка запуска веб-сервера: {hlex.Message}\nУбедитесь, что указанный IP-адрес {basePrefix} доступен и приложение имеет необходимые права.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Неизвестная ошибка при запуске веб-сервера: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }

        private static void HandleRequest(object state)
        {
            var context = state as HttpListenerContext;
            if (context == null)
                return;

            try
            {
                var response = context.Response;
                response.ContentEncoding = Encoding.UTF8;
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

                string responseString = "";
                string rawUrl = context.Request.RawUrl.TrimEnd('/').ToLower();

                switch (rawUrl)
                {
                    case "":
                        responseString = currentSong;
                        break;
                    case "/artist":
                        responseString = GetArtist();
                        break;
                    case "/title":
                        responseString = GetTitle();
                        break;
                    case "/translit":
                        responseString = Transliterate(currentSong);
                        break;
                    case "/rt":
                        responseString = $"\\+ar{Transliterate(GetArtist())}\\- - \\+ti{Transliterate(GetTitle())}\\-";
                        break;
                    default:
                        response.StatusCode = 404;
                        responseString = "Not Found";
                        break;
                }

                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                using (var output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                // Логирование ошибки обработки запроса
            }
        }

        /// <summary>
        /// Удаляет все круглые скобки и их содержимое из строки.
        /// Пример: "Песня (Live)" -> "Песня"
        /// </summary>
        /// <param name="text">Исходная строка</param>
        /// <returns>Строка без скобок и их содержимого</returns>
        private static string RemoveParenthesesContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Регулярное выражение для удаления содержимого в круглых скобках вместе со скобками
            return Regex.Replace(text, @"\s*\([^)]*\)", "").Trim();
        }


        private static string GetTrimmedTooltip(string text)
        {
            const int maxLength = 63; // Максимальная длина подписи в трее
            if (text.Length > maxLength)
            {
                return text.Substring(0, maxLength - 3) + "...";
            }
            return text;
        }

        private static void OpenWebServerUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Открывает URL в браузере по умолчанию
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть веб-сервер: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private static string GetArtist()
        {
            var parts = currentSong.Split(new string[] { " - " }, StringSplitOptions.None);
            return parts.Length > 0 ? parts[0] : "";
        }

        private static string GetTitle()
        {
            var parts = currentSong.Split(new string[] { " - " }, StringSplitOptions.None);
            return parts.Length > 1 ? parts[1] : "";
        }

        private static string Transliterate(string text)
        {
            // Расширенная транслитерация с поддержкой специальных символов
            var cyrillic = new[]
            {
                "А","Б","В","Г","Д","Е","Ё","Ж","З","И","Й","К","Л","М","Н","О","П","Р","С","Т",
                "У","Ф","Х","Ц","Ч","Ш","Щ","Ъ","Ы","Ь","Э","Ю","Я",
                "а","б","в","г","д","е","ё","ж","з","и","й","к","л","м","н","о","п","р","с","т",
                "у","ф","х","ц","ч","ш","щ","ъ","ы","ь","э","ю","я"
            };
            var latin = new[]
            {
                "A","B","V","G","D","E","E","Zh","Z","I","Y","K","L","M","N","O","P","R","S","T",
                "U","F","Kh","Ts","Ch","Sh","Shch","","Y","","E","Yu","Ya",
                "a","b","v","g","d","e","e","zh","z","i","y","k","l","m","n","o","p","r","s","t",
                "u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya"
            };

            for (int i = 0; i < cyrillic.Length; i++)
            {
                text = text.Replace(cyrillic[i], latin[i]);
            }
            return text;
        }
    }
}
