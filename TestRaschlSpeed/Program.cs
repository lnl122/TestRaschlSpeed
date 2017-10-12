// Copyright © 2016 Antony S. Ovsyannikov aka lnl122
// License: http://opensource.org/licenses/MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;

namespace TestRaschlSpeed
{
    class Program
    {
        // строка для расположения словаря
        //public static string dict_str = "";
        public static SQLiteConnection sql_con;
        public static SQLiteCommand sql_cmd;
        public static SQLiteDataAdapter DB;
        public static DataSet DS = new DataSet();
        public static DataTable DT = new DataTable();

        static void Main(string[] args)
        {
            // проверяемая строка
            string test = "скандинавы (2) самоуправствовать(3) невесомость(1) перечисление(3) подтверждение(1) новостройка(3) разбиться(1)";
            test = "лошадь (2) типаж(2) тапок(2)";
            // нормализуем строку в наш формат, и, готовим перечень слов с количествами букв для взятия из каждого из слов
            Raschl.OneStr os = Raschl.Prepare(Raschl.Normalize(test));

            // часы для замера скорости
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            // читаем файл словаря
            ReadWordDictionary(@"C:\TEMP\dict2.txt", @"C:\TEMP\dict2.db", Encoding.Unicode);
            //Console.WriteLine(dict_str.Length.ToString());
            stopWatch.Stop();
            TimeSpan ts1 = stopWatch.Elapsed;


            stopWatch.Restart();
            // выполняем поиск всех вариантов
            string[] wrds = Raschl.Transposition(os);
            stopWatch.Stop();
            TimeSpan ts2 = stopWatch.Elapsed;

            stopWatch.Restart();
            // убираем дубликаты
            string[] wrds2 = wrds;//.Distinct().ToArray();
            stopWatch.Stop();
            TimeSpan ts3 = stopWatch.Elapsed;

            Console.WriteLine("wrds.Count =                   " + wrds.Count());
            stopWatch.Restart();
            // ищем в словаре
            int len = wrds[1].Length;
            foreach(string ww in wrds)
            {
                //sql_cmd = sql_con.CreateCommand();
                //sql_cmd.CommandText = "CREATE TABLE Words ( wrd VARCHAR(50), len INTEGER)";
                //sql_cmd.ExecuteNonQuery();
                sql_cmd = sql_con.CreateCommand();
                sql_cmd.CommandText = "SELECT * FROM (SELECT * FROM Words WHERE len = " + len + ") Words2 INNER JOIN " +
                    " SELECT p1.q+p2.q FROM (SELECT (SELECT 'лоп' a UNION SELECT '111') p1 CROSS JOIN (SELECT (SELECT 'ата' a UNION SELECT '111') p2) Parts";
                SQLiteDataReader reader = sql_cmd.ExecuteReader();
                while (reader.Read())
                {
                    Console.WriteLine(reader["wrd"].ToString());
                }
            }
            stopWatch.Stop();
            TimeSpan ts4 = stopWatch.Elapsed;



            // выводим результаты замеров с сопутствующими метриками количеств слов
            WriteTimeSpan(    "read dictionary time =         ", ts1);
            WriteTimeSpan(    "search all variants time =     ", ts2);
            Console.WriteLine("wrds.Count =                   " + wrds.Count());
            WriteTimeSpan(    "kill dupes time =              ", ts3);
            Console.WriteLine("wrds2.Count (after Distinct) = " + wrds2.Count());
            WriteTimeSpan(    "searching time =               ", ts4);

            sql_con.Close();

            // ждем ввода для закрытия окна
            string k = Console.ReadLine();
        }

        /// <summary>
        /// читаем словарь в базу
        /// </summary>
        /// <param name="v">путь к тексту</param>
        /// <param name="v2">путь к базе</param>
        /// <param name="cp">кодепейдж текстового файла словаря</param>
        public static void ReadWordDictionary(string v, string v2, Encoding cp)
        {
            StreamReader dict = new StreamReader(v, cp);
            string dict_str = dict.ReadToEnd();
            List<string> dict_lst = dict_str.Split(' ').ToList();
            if (File.Exists(v2))
            {
                sql_con = new SQLiteConnection("Data Source=" + v2 + ";Version=3;New=False;Compress=True;");
                sql_con.Open();
            }
            else
            {
                sql_con = new SQLiteConnection("Data Source=" + v2 + ";Version=3;New=True;Compress=True;");
                sql_con.Open();
                sql_cmd = sql_con.CreateCommand();
                sql_cmd.CommandText = "CREATE TABLE Words ( wrd VARCHAR(50), len INTEGER)";
                sql_cmd.ExecuteNonQuery();
                Console.WriteLine("total words = " + dict_lst.Count.ToString());
                int cnt = 0;
                foreach(string str in dict_lst)
                {
                    string str2 = str.Trim().Replace("'","");
                    if(str2 == "") { continue; }
                    sql_cmd = sql_con.CreateCommand();
                    sql_cmd.CommandText = "insert into Words (wrd, len) values ('" + str2 + "', " + str2.Length + ")";
                    sql_cmd.ExecuteNonQuery();
                    cnt++;
                    if(cnt % 1000 == 0)
                    {
                        Console.WriteLine("current wrd is " + cnt.ToString() + " of " + dict_lst.Count.ToString() + " = " + Math.Floor(100.0 * cnt / dict_lst.Count).ToString() + " %");
                    }
                }
            }
            

        }

        /// <summary>
        /// вывести в консоль время выполнения
        /// </summary>
        /// <param name="str">текст</param>
        /// <param name="ts">TimeSpan</param>
        public static void WriteTimeSpan(string str, TimeSpan ts)
        {
            Console.WriteLine(str + String.Format("{0:00}:{1:00}:{2:00}.{3:000}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 100));
        }
    }

    public class Raschl
    {
        // структура расчлененки - для отдельных слов и количеств букв из них, тупо два массива, промежуточное хранение
        public struct OneStr
        {
            public string[] str;
            public int[] num;
        }

        /// <summary>
        /// готовит набор вероятных слов
        /// </summary>
        /// <param name="d">структура расчлененки</param>
        /// <returns>массив слов</returns>
        public static string[] Transposition(OneStr d)
        {
            // для пустых структур - выходим
            if (d.num.Length == 0) { return new string[0]; }

            // количество слов
            int words = d.str.Length;

            // d. = .str[], .num[]

            // текущие координаты
            int[] cur = new int[words];
            // длинна слов
            int[] sta = new int[words]; 
            // всего вариантов
            int total = 1;
            for (int i = 0; i < words; i++)
            {
                // для каждого слова - начальный счетчик его части = 0
                cur[i] = 0;
                // максимальное начало подстроки
                sta[i] = d.str[i].Length - d.num[i]; // максимальное начало строки, с нуля 0..ххх
                // счетчик количества вариантов
                total = total * (sta[i] + 1);
            }

            // подготовка частей слов, +20% скорости
            List<string[]> parts = new List<string[]>(words);
            for (int i = 0; i < words; i++)
            {
                // для каждого слова его подстроки храним в отдельном массиве
                string[] parts_one = new string[sta[i] + 1];
                for(int j = 0; j <= sta[i]; j++)
                {
                    parts_one[j] = d.str[i].Substring(j, d.num[i]);
                }
                // собираем все массивы в один список массивов
                parts.Add(parts_one);
            }

            // массив для всех вариантов слов
            string[] allwrds = new string[total];
            // текущий индекс
            int curwrd = 0;

            // здесь собираем текущий вариант
            StringBuilder r3 = new StringBuilder();
            while (cur[words - 1] <= sta[words - 1])
            {
                r3.Clear();
                //r3.Append(" ");
                // (!) если тело цикла собрать в { } - удивительно что производительнсть будет ниже на 20%
                for (int i = 0; i < words; i++)
                    r3.Append(parts[i][cur[i]]);

                // добавим найденной слово, увеличиваем счетчик
                // (?) м.б. делать массив stringBuilder'ов, чтоб не тратить время на перевод в строку?
                //r3.Append(" ");
                allwrds[curwrd] = r3.ToString();
                curwrd++;

                // корректируем индексы частей слов
                cur[0]++; // увеличим начальный
                for (int i = 0; i < words - 1; i++)
                {
                    if (cur[i] > sta[i]) // если он преодолел максимум - корректируем его =0 и следующий ++
                    {
                        cur[i] = 0;
                        cur[i + 1]++;
                    }
                }
            }//while

            return allwrds;
        }

        /// <summary>
        /// готовит структуру слов в массива для решения
        /// </summary>
        /// <param name="s1">строка, число слов</param>
        /// <returns>структура из двух массивов</returns>
        public static OneStr Prepare(string s1)
        {
            OneStr res = new OneStr();

            // (?) нафиг использовать регулярки, когда у строки есть аналогичный метод?
            string[] t3 = Regex.Split(s1, "\\)");
            string[] wrds = new string[t3.Length - 1];
            int[] nums = new int[t3.Length - 1];
            for (int i = 0; i < t3.Length - 1; i++)
            {
                string t4 = t3[i];
                string[] t5 = Regex.Split(t4, "\\(");
                wrds[i] = t5[0];
                int rr = 0;
                // (?) а если в скобках не число - то запишется 0, что тогда будет с дальнейшей обработкой?
                // (?) substring длинны 0 нельзя получить, словим эксепшн
                // (?) по идее надо вернуть пустую структуру, или генерировать эксепшн здесь, а выше его обрабатывать
                Int32.TryParse(t5[1], out rr);
                nums[i] = rr;
            }
            // собрать данные в структуры по одной на строку
            res.num = nums;
            res.str = wrds;
            return res;
        }

        /// <summary>
        /// нормализует входные данные в формат
        /// "строитель(3)блеф(2)картон(2)#жироприказ(4)слюда(2)чемодан(2)гарнир(2)лезвие(1)#житель(3)тепло(2)рогожа(3)мрак(2)мозг(1)карман(2)##"
        /// </summary>
        /// <param name="d">строка</param>
        /// <returns>нормализованная строка или пустая - если некорректный формат</returns>
        public static string Normalize(string d)
        {
            string t0 = d.ToLower().Replace(" ", "").Replace(",", "").Replace("\r\n", "#").Replace("###", "##").Replace("###", "##").Replace("###", "##").Replace("###", "##");
            t0 = (t0 + "##").Replace("###", "##").Replace("###", "##");
            //t1 = строитель(3)блеф(2)картон(2)#жироприказ(4)слюда(2)чемодан(2)гарнир(2)лезвие(1)#житель(3)тепло(2)рогожа(3)мрак(2)мозг(1)карман(2)##
            //t1 = строитель(3)#блеф(2)#картон(2)##жироприказ(4)#слюда(2)#чемодан(2)#гарнир(2)#лезвие(1)##
            // определим тип входного данного
            int s1 = t0.Length - t0.Replace(")", "").Length;        // правые скобки
            int s12 = t0.Length - t0.Replace("(", "").Length;       // левые скобки
            int s2 = (t0.Length - t0.Replace(")#", "").Length) / 2; // после каждой правой скобки - новая строка - сколько раз
            if ((s1 == 0) || (s1 != s12)) { return ""; }            // если нет правых скобок вообще или их количество не равно числу левых скобок
            string[] t2 = System.Text.RegularExpressions.Regex.Split(t0, "\\(");
            int res;
            bool fl = true;
            for (int i = 1; i < t2.Length; i++)
            {
                string[] t4 = System.Text.RegularExpressions.Regex.Split(t2[i], "\\)");
                fl = fl && Int32.TryParse(t4[0], out res);
            }
            if (!fl) { return ""; }                                 // если внутри скобок есть не число
            if (s1 == s2)
            {
                // type строитель(3)#блеф(2)#картон(2)##жироприказ(4)#слюда(2)#чемодан(2)#гарнир(2)#лезвие(1)##
                t0 = t0.Replace("#", "$").Replace("$$", "#").Replace("$", "");
            }
            else
            {
                // type строитель(3)блеф(2)картон(2)#жироприказ(4)слюда(2)чемодан(2)гарнир(2)лезвие(1)#житель(3)тепло(2)рогожа(3)мрак(2)мозг(1)карман(2)##
            }
            t0 = t0.Replace("##", "#").Replace("##", "#");
            return t0; // or "" above by text
        }                   // нормализация вида задачи


    }
}
