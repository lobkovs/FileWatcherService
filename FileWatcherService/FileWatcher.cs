using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using NLog;

namespace FileWatcherService
{
    class FileWatcher
    {
        Logger log = LogManager.GetCurrentClassLogger();

        FileSystemWatcher watcher;
        long offsetBytes = 0;

        List<string> detectMetrics = new List<string>();
        MetricsApi MetricsApi;

        string watchDir = Properties.Settings.Default.watchDir;
        string watchFilter = Properties.Settings.Default.watchFilter;
        string metricsFilePath = Properties.Settings.Default.metricsFilePath;

        public FileWatcher()
        {
            log.Debug("Инициализируем File System Watcher.");

            watcher = new FileSystemWatcher();
            watcher.Path = watchDir;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            watcher.Filter = watchFilter;

            // Зарегистрируем обработчик событий изменения
            watcher.Changed += new FileSystemEventHandler(Watcher_Changed);

            // Посчитаем кол-во байт при первом запуске для последующего смещения, чтобы не читать каждый раз сначала файла.
            init();
            // Инициализируем Api метрик
            MetricsApi = new MetricsApi(metricsFilePath);
        }

        public void Start()
        {
            watcher.EnableRaisingEvents = true;
            log.Info("File System Watcher запущен.");
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
            log.Info("File System Watcher остановлен.");
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            using (FileStream fstream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fstream.Position = offsetBytes;

                StreamReader sr = new StreamReader(fstream);
                string line;

                while ((line = sr.ReadLine()) != null)
                {
                    #region Проверка ошибок

                    // /////////////////
                    // Check on error
                    // /////////////////
                    if (Regex.IsMatch(line, Properties.Settings.Default.regexpError, RegexOptions.IgnoreCase))
                    {
                        DateTime lineTime = getTimeFromLogText(line);

                        log.Error("Найдена ошибка в \"{0}\" в строке \"{1}\"!", lineTime, line);

                        sendToZabbixString(lineTime, line, e.FullPath);
                        continue;
                    }
                    #endregion

                    #region Проверка метрик

                    // /////////////////
                    // Check metrics
                    // /////////////////

                    if (MetricsApi.CheckLine(line))
                    {
                        log.Debug("Линия \"{0}\" входит в список метрик.", line);
                        // Получим имя метрики из конфигурационного файла
                        string q = MetricsApi.GetDetectMetric(line);
                        log.Trace("Оригинальная метрика: \"{0}\"", q);
                        // Добавим в массив найденных метрик
                        detectMetrics.Add(q);

                        // Проверяем существование элемента в конфигурационном файле
                        if (MetricsApi.ExistElem(detectMetrics))
                        {
                            log.Debug("Элемент \"{0}\", обнаружен в конф. файле.", detectMetrics.Aggregate((a,b) => a + "; " + b));
                            // Если последний(целевой) элемент тогда сформируем и отправим данные в заббикс
                            if (MetricsApi.IsLast(detectMetrics))
                            {
                                log.Trace("Элемент \"{0}\", последний в ветке в конф. файле.", detectMetrics.Aggregate((a, b) => a + "; " + b));
                                // Получим число из строки лога
                                int digit = getDigitFromString(line);

                                log.Debug("Для строки \"{0}\", сформировано число: \"{1}\".", line, digit);

                                // Для отправки полученное числовое значение должно быть больше "0"
                                if (digit > 0)
                                {
                                    Console.WriteLine("Линия: \"{0}\". Число: \"{1}\".", line, digit);

                                    // Сформируем заббикс метрику из массива найденной метрики
                                    string zabbixMetric = MetricsApi.getZabbixMetricName(detectMetrics);

                                    log.Debug("Для списка метрик \"{0}\", сформирована метка: \"{1}\".", detectMetrics.Aggregate((a, b) => a + "; " + b + ";"), zabbixMetric);

                                    // Отправим результат в Zabbix
                                    sendToZabbixNumeric(zabbixMetric, digit);
                                }

                                if (MetricsApi.ExistNextSibling(detectMetrics))
                                    // У элемента есть следующий по счёту сосед, удалим последний элемент, тем самым оставим место для соседа
                                    detectMetrics.Remove(q);
                                else
                                    // Сосед отсутствует, значит в этой секции все метрики найдены, очистим список найденных метрик, чтобы начать поиск заново
                                    detectMetrics.Clear();

                            }
                        }
                        // Метрика не найдена в конфигурационном файле, тогда проверим есть ли родитель у элемента,
                        // если нет тогда это корневой элемент и началась новая секция
                        else if (!MetricsApi.ExistParent(q))
                        {
                            // Очистим список вообще
                            detectMetrics.Clear();
                            // Добавим сразу корневой элемент
                            detectMetrics.Add(q);
                        }
                        else
                        {
                            // Дубль. Удалим его
                            detectMetrics.Remove(q);

                            #region Может понадобится
                            // Элемент не существует в конфигурационном файле вообще, можно предположить что необходимо начать новую секцию, для этого:
                            // Перевернём список
                            //detectMetrics.Reverse();
                            // Запомним последний элемент
                            //string lastElem = detectMetrics[0];
                            // Очистим список вообще
                            //detectMetrics.Clear();
                            // Добавим последний элемент вначало
                            //detectMetrics.Add(lastElem);
                            #endregion
                        }

                    }

                    #endregion

                }

                // Сместим позицию, чтобы в следующий раз прочитать файл с последнего места
                offsetBytes = fstream.Position;
            }
        }

        /// <summary>
        /// Подсчёт кол-ва байт для целевого файла
        /// </summary>
        public void init()
        {
            string fullpath = string.Format("{0}\\{1}", watchDir, watchFilter);
            log.Debug("Инициализируем начальные переменные, такие как отступ строк ...");
            log.Trace("Целевой файл: {0}", fullpath);

            if (File.Exists(fullpath))
            {
                // Посчитаем кол-во байт в файле изначально
                offsetBytes = new FileInfo(fullpath).Length;

                log.Trace("Кол-во байт в файле: {0}", offsetBytes);
            }
            else
                log.Error("Файла \"{0}\" не существует!", fullpath);
        }

        /// <summary>
        /// Отправляет в Zabbix числовые значения
        /// </summary>
        /// <param name="metricName"></param>
        /// <param name="value"></param>
        public void sendToZabbixNumeric(string metricName, int value)
        {
            // Сформируем key, value
            string args = string.Format("-k {0} -o {1}", metricName, value);

            log.Trace("Сформированы аргументы: \"{0}\".", args);

            // Send to zabbix
            sendToZabbix(args);
        }

        /// <summary>
        /// Отправляет в Zabbix строковые значения
        /// </summary>
        /// <param name="time"></param>
        /// <param name="line"></param>
        public void sendToZabbixString(DateTime time, string line, string fileName)
        {
            // Сформируем value
            string value = string.Format("Error is in \"{0}\" time in file \"{1}\"", time, fileName);
            // Сформируем аргументы
            string args = string.Format("-k xLoad.error -o \"{0}\"", value);

            log.Trace("Сформированы аргументы: \"{0}\".", args);

            // Send to zabbix
            sendToZabbix(args);
        }

        /// <summary>
        /// Отправляет данные в заббикс
        /// </summary>
        /// <param name="keyValue"></param>
        private void sendToZabbix(string keyValue)
        {
            // Проверим, можно ли нам вообще отправлять в Zabbix
            if (Properties.Settings.Default.zabbixEnableSend == false)
                return;

            // Проинициализируем переменные
            string zabbixSenderPath = Properties.Settings.Default.zabbixSenderPath;
            string zabbixServer = Properties.Settings.Default.zabbixServer;
            string zabbixNetworkNode = Properties.Settings.Default.zabbixNetworkNode;

            // Сформируем окончательные аргументы для zabbix sender'а
            string finalZabbixArgs = string.Format("-z {0} -s {1} {2}", zabbixServer, zabbixNetworkNode, keyValue);

            log.Trace("Финальные аргументы для Zabbix sender'а: \"{0}\".", finalZabbixArgs);

            try
            {
                // Отложим страрт отправки чтобы снизить возможную нагрузку
                Thread.Sleep(2000);
                // Вызовем Zabbix агента с аргументами
                Process.Start(zabbixSenderPath, finalZabbixArgs);
                log.Info(string.Format("Аргументы \"{0}\" в Zabbix отправлены!", finalZabbixArgs));
            }
            catch (Exception ex)
            {
                log.Error("Не удалось отправить в Zabbix аргументы \"{0}\". Описание ошибки: {1}", finalZabbixArgs, ex.Message);
            }
        }

        /// <summary>
        /// Возвращает число из строки
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public int getDigitFromString(string input)
        {
            int output = 0;
            try {
                // Получаем число в строковом виде из входной строки
                string sDigit = new String(input.Where(Char.IsDigit).ToArray());
                // Пробуем преобразовать в числовой формат (int)
                output = Convert.ToInt32(sDigit);
                log.Trace("Из строки \"{0}\" получено число: \"{1}\"", input, output);
            } catch(Exception ex) {
                // Не получилось преобразовать! :(
                // Выводим в лог ошибку, но при этом все равно возвращаем заранее определённое значение равное "0"
                log.Warn("Произошла ошибка при попытке получить число из строки \"{0}\". Описание ошибки: {1}", input, ex.Message);
                log.Trace("Возвращаю число по умолчанию, равное \"{0}\"", output);
            }
            return output;
        }

        /// <summary>
        /// Пытается извлеч время из строки
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private DateTime getTimeFromLogText(string line)
        {
            string regexpPatterTime = @"^\d{4}\.\d{2}\.\d{2}\s\d{2}:\d{2}:\d{2}";
            DateTime detectTime;

            if (DateTime.TryParseExact(Regex.Match(line, regexpPatterTime).Value, "yyyy.MM.dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out detectTime))
            {
                log.Trace("Время \"{0}\" получено при парсинге \"{1}\".", detectTime, line);
                return detectTime;
            }
            else
            {
                log.Trace("Не удалось получить время из строки \"{0}\". Установим текущее время.", line);
                return DateTime.Now;
            }
        }
    }
}