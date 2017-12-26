using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using NLog;
using Newtonsoft.Json;

namespace FileWatcherService
{
    class MetricsApi
    {
        private List<string> flatListMetrics = new List<string>();
        private Logger log = LogManager.GetCurrentClassLogger();
        private XmlDocument xMetrics = new XmlDocument();

        public MetricsApi(string path)
        {
            // Проверка существования конф. файла
            if (File.Exists(path))
            {
                log.Debug("Файл {0} существует!", path);
                // Загрузка(парсинг) конф. файла в XML
                xMetrics.Load(path);

                // Получим все элементы с аттрибутом "name" для построения "плоского" списка
                XmlNodeList namesList = xMetrics.SelectNodes("//@name");

                // Создаём плоский список для поиска вхождений, чтобы не пробегаться по всему оригинальному XML конф. файлу
                foreach (XmlNode name in namesList)
                {
                    string nameValue = name.Value;
                    // Если несуществует такого значения в списке, тогда добавим.
                    // Тем самым мы получим "плоский" список, только уникальных значений
                    if (!flatListMetrics.Exists(x => x == nameValue))
                        flatListMetrics.Add(name.Value);
                }
            }
            else
            {
                log.Error("Конфигурационного файла по адресу \"{0}\" для метрик НЕ существует!", path);
            }
        }

        /// <summary>
        /// Проверка вхождения строки в массив метрик
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public bool CheckLine(string line)
        {
            // Проверяем наличие(вхождение) переданного аргумента в "плоском" списке метрик из конф. файла
            return flatListMetrics.Exists(x => Regex.IsMatch(line, x, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Возвращает имя метрики из конфигурационного файла
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public string GetDetectMetric(string line)
        {
            // Ищем в "плоском" списке метрик конф. файла вхождение переданного аргумента
            return flatListMetrics.Find(x => Regex.IsMatch(line, x, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Проверяет является ли элемент последним в ветке
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        public bool IsLast(List<string> tree)
        {
            // соберём xPath
            string xPath = GetXPath(tree);
            // Выберем элемент
            XmlNode node = xMetrics.SelectSingleNode(xPath);
            return !node.HasChildNodes;
        }

        /// <summary>
        /// Строит xPath для входного списка
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        private string GetXPath(List<string> tree)
        {
            // Временный массив с изменёнными значениями
            List<string> temp = new List<string>();
            // Добавление во временный массив новых значений
            tree.ForEach(x => temp.Add("*[@name='" + x + "']"));
            // Склеим и вернём строку
            return temp.Aggregate((x, y) => x + "/" + y);
        }

        /// <summary>
        /// Проверяет существование элемента
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        public bool ExistElem(List<string> tree)
        {
            // Сравниваем полученный элемента с значеним NULL, возвращаем результат
            return xMetrics.SelectSingleNode(GetXPath(tree)) != null;
        }

        /// <summary>
        /// Проверяет существование родителя
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public bool ExistParent(string elem)
        {
            // Формируем xPath
            string xPath = "//*[@name='" +  elem + "']";
            log.Trace("xPath = {0}", xPath);
            // Получаем элемент по xPath
            XmlNode xElem = xMetrics.SelectSingleNode(xPath);
            // Записывает тип полученного элемента
            string parentType = xElem.ParentNode.NodeType.ToString();
            log.Trace("parentType = {0}", parentType);
            log.Trace("parentType == \"Element\" --> {0}", parentType == "Element");
            // Возвращаем результат
            return parentType == "Element";
        }

        /// <summary>
        /// Проверяет существует ли у элемента следующий по счёту сосед
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        public bool ExistNextSibling(List<string> tree)
        {
            XmlNode currentElem = xMetrics.SelectSingleNode(GetXPath(tree));
            log.Trace("currentElem.NextSibling != null --> {0}", currentElem.NextSibling != null);
            return currentElem.NextSibling != null;
        }

        /// <summary>
        /// Возвращает название Zabbix метки для текущей метки
        /// </summary>
        /// <param name="tree"></param>
        /// <returns></returns>
        public string getZabbixMetricName(List<string> tree)
        {
            // Создадим внутренний рабочий список из оригинального
            List<string> tempList = new List<string>();
            // Перевернём список, чтобы взять первые два элемента, которые изначально были последними
            tree.Reverse();
            // Обработаем список
            tree
                // Найдем первые два элемента
                .FindAll(x => tree.IndexOf(x) <= 1)
                // Добавим эти два элемента в новый временный список,
                // попутно заменим "пробел" на нижнее подчёркивание
                .ForEach(y => tempList.Add(y.Replace(" ", "_")));
            // Перевернём обратно, чтобы сохранить оригинальную последовательность
            tree.Reverse();
            // Конкатинируем элементы
            string metricName = tempList.Aggregate((a, b) => b + "." + a);
            // Заменим пробелы на нижние подчёркивания и вернём результат
            return metricName;
        }
    }
}
