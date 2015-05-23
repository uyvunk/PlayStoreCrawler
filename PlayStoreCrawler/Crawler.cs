using System;
using System.IO;
using System.Linq;
using BDC.BDCCommons;
using SharedLibrary;
using SharedLibrary.MongoDB;
using WebUtilsLib;
using System.Text.RegularExpressions;
using System.Threading;

namespace PlayStoreCrawler
{
    class Crawler
    {
        /// <summary>
         /// Entry point of the crawler
         /// </summary>
         /// <param name="args"></param>
        public static void Main (string[] args)
        {
            // TODO: vukn
            // 1.   need to add 2 options, 1 for reading the input from the file, and the second one would
            //      using the ordinary way to crawl
            // 2.   if the user want to read from file, get the path and read it. Separate criteria
            //      on each line of input file ( one criteria per line)
            // 3.   Call the helper function CrawlStore on the criteria

            // Give 2 options, and ask for user input
            int option = 0;
            do
            {
                System.Console.WriteLine("Please enter 1 to read from a file.");
                System.Console.WriteLine("OR enter 2 to do an automatic crawl.");
                option = Convert.ToInt32(Console.ReadLine());   // read in option
            } while (option != 1 && option != 2);

            // If user want to read content from file:
            if (option == 1)
            {
                // Asking user for the location of the input text file
                string path;
                do
                {
                    System.Console.WriteLine("Enter full path to the Input text file");
                    System.Console.WriteLine();
                    path = Console.ReadLine();
                } while (path == null);

                // when input is not null, start to read from the input
                try
                {
                    // read all lines of the input into 1 array lines
                    string[] lines = System.IO.File.ReadAllLines(path);

                    // Display the file contents by using a foreach loop.
                    System.Console.WriteLine("Reading the input . . .  ");
                    // Read the file line per line, and call the CrawlStore on each line
                    foreach (string line in lines)
                    {
                        Console.WriteLine("Crawling category:\t" + line);
                        // Call the helper with the correct category
                        CrawlStore(line);
                    }
                }
                catch (IOException)
                {
                    Console.WriteLine("File doesnt exist in : " + path);
                }

            }
            else    // If user want to use Ordinary way
            {
                // Crawling App Store using all characters as the Search Input
                CrawlStore("a");
                CrawlStore("b");
                CrawlStore("c");
                CrawlStore("d");
                CrawlStore("e");
                CrawlStore("f");
                CrawlStore("g");
                CrawlStore("h");
                CrawlStore("i");
                CrawlStore("j");
                CrawlStore("K");
                CrawlStore("L");
                CrawlStore("M");
                CrawlStore("N");
                CrawlStore("O");
                CrawlStore("P");
                CrawlStore("Q");
                CrawlStore("R");
                CrawlStore("S");
                CrawlStore("T");
                CrawlStore("U");
                CrawlStore("V");
                CrawlStore("X");
                CrawlStore("Y");
                CrawlStore("Z");
                CrawlStore("W");
                /// ... Keep Adding characters / search terms in order to increase the crawler's reach
                // APP CATEGORIES
                CrawlStore("BOOKS");
                CrawlStore("BUSINESS");
                CrawlStore("COMICS");
                CrawlStore("COMMUNICATION");
                CrawlStore("EDUCATION");
                CrawlStore("ENTERTAINMENT");
                CrawlStore("FINANCE");
                CrawlStore("HEALTH");
                CrawlStore("LIFESTYLE");
                CrawlStore("LIVE WALLPAPER");
                CrawlStore("MEDIA");
                CrawlStore("MEDICAL");
                CrawlStore("MUSIC");
                CrawlStore("NEWS");
                CrawlStore("PERSONALIZATION");
                CrawlStore("PHOTOGRAPHY");
                CrawlStore("PRODUCTIVITY");
                CrawlStore("SHOPPING");
                CrawlStore("SOCIAL");
                CrawlStore("SPORTS");
                CrawlStore("TOOLS");
                CrawlStore("TRANSPORTATION");
                CrawlStore("TRAVEL");
                CrawlStore("WEATHER");
                CrawlStore("WIDGETS");
                CrawlStore("ARCADE");
                CrawlStore("BRAIN");
                CrawlStore("CASUAL");
                CrawlStore("CARDS");
                CrawlStore("RACING");
            }
        }

        /// <summary>
        /// Executes a Search using the searchField as the search parameter, 
        /// paginates / scrolls the search results to the end adding all the url of apps
        /// it finds to a AWS SQS queue
        /// </summary>
        /// <param name="searchField"></param>
        private static void CrawlStore (string searchField)
        {
            // Console Feedback
            Console.WriteLine ("Crawling Search Term : [ " + searchField + " ]");

            // Compiling Regular Expression used to parse the "pagToken" out of the Play Store
            Regex pagTokenRegex = new Regex (@"GAEi+.+\:S\:.{11}\\42", RegexOptions.Compiled);

            // HTML Response
            string response;

            // MongoDB Helper
            // Configuring MongoDB Wrapper
            MongoDBWrapper mongoDB   = new MongoDBWrapper ();
            string fullServerAddress = String.Join (":", Consts.MONGO_SERVER, Consts.MONGO_PORT);
            mongoDB.ConfigureDatabase (Consts.MONGO_USER, Consts.MONGO_PASS, Consts.MONGO_AUTH_DB, fullServerAddress, Consts.MONGO_TIMEOUT, Consts.MONGO_DATABASE, Consts.MONGO_COLLECTION);

            // Ensuring the database has the proper indexe
            mongoDB.EnsureIndex ("Url");

            // Response Parser
            PlayStoreParser parser = new PlayStoreParser (); 

            // Executing Web Requests
            using (WebRequests server = new WebRequests ())
            {
                // Creating Request Object
                server.Host = Consts.HOST;

                // Executing Initial Request
                response    = server.Post (String.Format (Consts.CRAWL_URL, searchField), Consts.INITIAL_POST_DATA);

                // Parsing Links out of Html Page (Initial Request)                
                foreach (string url in parser.ParseAppUrls (response))
                {
                    // Checks whether the app have been already processed 
                    // or is queued to be processed
                    if ((!mongoDB.AppProcessed (Consts.APP_URL_PREFIX + url)) && (!mongoDB.AppQueued (url)))
                    {
                        // Console Feedback
                        Console.WriteLine (" . Queued App");

                        // Than, queue it :)
                        mongoDB.AddToQueue (url);
                        Thread.Sleep (250); // Hiccup
                    }
                    else
                    {
                        // Console Feedback
                        Console.WriteLine (" . Duplicated App. Skipped");
                    }
                }

                // Executing Requests for more Play Store Links
                int initialSkip       = 48;
                int currentMultiplier = 1;
                int errorsCount       = 0;
                do
                {
                    // Finding pagToken from HTML
                    var rgxMatch = pagTokenRegex.Match (response);

                    // If there's no match, skips it
                    if (!rgxMatch.Success)
                    {
                        break;
                    }

                    // Reading Match from Regex, and applying needed replacements
                    string pagToken = rgxMatch.Value.Replace (":S:", "%3AS%3A").Replace("\\42", String.Empty).Replace(@"\\u003d", String.Empty);

                    // Assembling new PostData with paging values
                    string postData = String.Format (Consts.POST_DATA, pagToken);

                    // Executing request for values
                    response = server.Post (String.Format (Consts.CRAWL_URL, searchField), postData);

                    // Checking Server Status
                    if (server.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        LogWriter.Error ("Http Error", "Status Code [ " + server.StatusCode + " ]");
                        errorsCount++;
                        continue;
                    }

                    // Parsing Links
                    foreach (string url in parser.ParseAppUrls (response))
                    {
                        // Checks whether the app have been already processed 
                        // or is queued to be processed
                        if ((!mongoDB.AppProcessed (Consts.APP_URL_PREFIX + url)) && (!mongoDB.AppQueued (url)))
                        {
                            // Console Feedback
                            Console.WriteLine (" . Queued App");

                            // Than, queue it :)
                            mongoDB.AddToQueue (url);
                            Thread.Sleep (250); // Hiccup
                        }
                        else
                        {
                            // Console Feedback
                            Console.WriteLine (" . Duplicated App. Skipped");
                        }
                    }

                    // Incrementing Paging Multiplier
                    currentMultiplier++;

                }  while (parser.AnyResultFound (response) && errorsCount <= Consts.MAX_REQUEST_ERRORS);
            }
        }
    }
}
