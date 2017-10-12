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
            test = "авторитарный (2) надпороть(2) головоногие(1) жаркое(1) стлать(2) одноклассник(3) кофейник(1) захаркать(1)";
            //test = "Принц (2) Нерадивец (3) Идеал (2) Барсук (1) Мораль (3) Проныра (1) Ведомость (4)";
            // нормализуем строку в наш формат, и, готовим перечень слов с количествами букв для взятия из каждого из слов
            Raschl.OneStr os = Raschl.Prepare(Raschl.Normalize(test));

            // часы для замера скорости
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            // читаем файл словаря
            ReadWordDictionary(@"C:\TEMP\dict2.txt", @"C:\TEMP\dict2.db", Encoding.Unicode);
            stopWatch.Stop();
            WriteTimeSpan("reading dictionary time =      ", stopWatch.Elapsed);

            Solve("Принц (2) Нерадивец (3) Идеал (2) Барсук (1) Мораль (3) Проныра (1) Ведомость (4)");
            Solve("скандинавы (2) самоуправствовать(3) невесомость(1) перечисление(3) подтверждение(1) новостройка(3) разбиться(1)");
            Solve("авторитарный (2) надпороть(2) головоногие(1) жаркое(1) стлать(2) одноклассник(3) кофейник(1) захаркать(1)");

            sql_con.Close();
            Console.WriteLine(" ");
            Console.WriteLine("it's all.. press any key to quit..");
            // ждем ввода для закрытия окна
            string k = Console.ReadLine();
        }

        private static void Solve(string v)
        {
            Console.WriteLine(" ");
            Console.WriteLine("Task: " + v);

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            sql_cmd = sql_con.CreateCommand();
            string query = Raschl.GetSqlStr(Raschl.Prepare(Raschl.Normalize(v)));
            sql_cmd.CommandText = query;
            stopWatch.Stop();
            Console.WriteLine(" ");
            WriteTimeSpan("prepare data for solve =       ", stopWatch.Elapsed);

            stopWatch.Restart();
            SQLiteDataReader reader = sql_cmd.ExecuteReader();
            stopWatch.Stop();
            WriteTimeSpan("sqlite job time =              ", stopWatch.Elapsed);

            stopWatch.Restart();
            DataSet ds_res = new DataSet();
            ds_res.Tables.Add("wrd");
            ds_res.Tables[0].Load(reader);
            List<string> res = new List<string>();
            foreach (DataRow drc in ds_res.Tables[0].Rows)
            {
                res.Add(drc[0].ToString());
            }
            stopWatch.Stop();
            WriteTimeSpan("reading result table time =    ", stopWatch.Elapsed);
            Console.WriteLine(" ");

            foreach(string ss in res)
            {
                Console.WriteLine(ss);
            }
            //Console.WriteLine(" ");
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
            Console.WriteLine(str + String.Format("{0:00}:{1:00}:{2:00}.{3:000}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds));
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
        /// готовит строку для запроса
        /// </summary>
        /// <param name="d">структура расчлененки</param>
        /// <returns>часть скуль запроса</returns>
        public static string GetSqlStr(OneStr d)
        {
            // для пустых структур - выходим
            if (d.num.Length == 0) { return ""; }

            // количество слов
            int words = d.str.Length;

            // d. = .str[], .num[]

            // текущие координаты
            int[] cur = new int[words];
            // длинна слов
            int[] sta = new int[words];
            // всего вариантов
            int total = 1;
            int common_len = 0;
            for (int i = 0; i < words; i++)
            {
                // для каждого слова - начальный счетчик его части = 0
                cur[i] = 0;
                // максимальное начало подстроки
                sta[i] = d.str[i].Length - d.num[i]; // максимальное начало строки, с нуля 0..ххх
                // счетчик количества вариантов
                total = total * (sta[i] + 1);
                common_len += d.num[i];
            }
            Console.WriteLine("total = " + total.ToString());

            // "SELECT * FROM Words INNER JOIN 
            // (SELECT a1 || a2 || a3 tst FROM 
            //string res = "SELECT tst wrd FROM (SELECT a1 ";
            string res = "SELECT * FROM (SELECT a1";
            for (int i = 1; i < words; i++)
            {
                res = res + " || a" + (i + 1).ToString();
            }
            res = res + " tst FROM ";

            // подготовка частей слов
            List<string[]> parts = new List<string[]>(words);
            for (int i = 0; i < words; i++)
            {
                // для каждого слова его подстроки храним в отдельном массиве
                string[] parts_one = new string[sta[i] + 1];
                for (int j = 0; j <= sta[i]; j++)
                {
                    parts_one[j] = d.str[i].Substring(j, d.num[i]);
                }
                // собираем все массивы в один список массивов
                parts.Add(parts_one);
            }

            // (SELECT 'ло' a1 UNION SELECT 'лъ') p1 
            res = res + "(SELECT '" + parts[0][0] + "' a1 ";
            for (int i = 1; i < parts[0].Length; i++)
            {
                res = res + " UNION SELECT '" + parts[0][i] + "'";
            }
            res = res + ") p1 ";

            for(int w = 1; w < words; w++)
            {
                // CROSS JOIN (SELECT 'па' a2 UNION SELECT 'йй') p2
                // CROSS JOIN (SELECT 'хх' a3 UNION SELECT 'та') p3
                res = res + "CROSS JOIN (SELECT '" + parts[w][0] + "' a" + (w + 1).ToString() + " ";
                for (int i = 1; i < parts[w].Length; i++)
                {
                    res = res + " UNION SELECT '" + parts[w][i] + "'";
                }
                res = res + ") p" + (w + 1).ToString() + " ";
            }

            // ) pp ON pp.tst=Words.wrd";
            //res = res + ") ";
            res = res + ") pp INNER JOIN w" + common_len.ToString() + " ww ON pp.tst = ww.wrd";
            //res = res + ") pp INNER JOIN Words ww ON pp.tst = ww.wrd";
            //res = res + ") pp INNER JOIN (SELECT * FROM Words WHERE len = " + common_len + ") ww ON pp.tst = ww.wrd";

            return res;
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
