using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using Topshelf;
using NLog;

// TODO:
//   Проверить изменение конфига в реальном времени

namespace FileWatcherService
{
    public class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        public static void Main()
        {
            Logger log = LogManager.GetCurrentClassLogger();
            log.Debug("#################################");
            log.Debug("Начало выполнение программы!");

            log.Debug("Запускаем программу как службу Windows!");
            HostFactory.Run(x =>
            {
                x.StartManually();

                x.Service<FileWatcher>(s =>
                {
                    s.ConstructUsing(hostSettings => new FileWatcher());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });
                x.RunAsLocalSystem();

                x.SetDescription(string.Format(@"Отслеживает выполнение процесса XLoad и отправляет метрики в Zabbix! Служба смонтирована в папке {0}.", Directory.GetCurrentDirectory()));
                x.SetDisplayName("XLoadWatch");
                x.SetServiceName("XLoadWatch");

            });
        }
    }
}
