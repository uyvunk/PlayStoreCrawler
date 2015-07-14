using System;
using System.Linq;
using System.Threading;
using BDC.BDCCommons;
using SharedLibrary;
using SharedLibrary.Models;
using SharedLibrary.MongoDB;
using WebUtilsLib;
using System.Diagnostics;

namespace PlayStoreWorker
{
    class Worker
    {
        /// <summary>
        /// Entry point of the worker piece of the process
        /// Notice that you can run as many workers as you want to in order to make the crawling faster
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            // Configuring Log Object Threshold
            LogWriter.Threshold = TLogEventLevel.Information;
            LogWriter.Info ("Worker Started");

            // Parser
            PlayStoreParser parser = new PlayStoreParser();

            // Configuring MongoDB Wrapper
            MongoDBWrapper mongoDB   = new MongoDBWrapper();
            string fullServerAddress = String.Join(":", Consts.MONGO_SERVER, Consts.MONGO_PORT);
            mongoDB.ConfigureDatabase(Consts.MONGO_USER, Consts.MONGO_PASS, Consts.MONGO_AUTH_DB, fullServerAddress, Consts.MONGO_TIMEOUT, Consts.MONGO_DATABASE, Consts.MONGO_COLLECTION);

            // Creating Instance of Web Requests Server
            WebRequests server = new WebRequests ();
            
            QueuedApp app;

            // Retry Counter (Used for exponential wait increasing logic)
            int retryCounter = 0;

            // Iterating Over MongoDB Records while no document is found to be processed                
            while ((app = mongoDB.FindAndModify ()) != null)
            {
                try
                {
                    // Building APP URL
                    string appUrl = Consts.APP_URL_PREFIX + app.Url;

                    // Checking if this app is on the database already
                    if (mongoDB.AppProcessed(appUrl))
                    {
                        // Console Feedback, Comment this line to disable if you want to
                        Console.WriteLine("Duplicated App, skipped.");

                        // Delete it from the queue and continues the loop
                        mongoDB.RemoveFromQueue (app.Url);
                        continue;
                    }

                    // Vu
                    // Check if the app does not meet criteria
                    if (app.NotMeetCrit)
                    {
                        Console.WriteLine("App Not meet Criteria, Skipped.");
                    }

                    // Configuring server and Issuing Request
                    server.Headers.Add (Consts.ACCEPT_LANGUAGE);
                    server.Host              = Consts.HOST;
                    server.Encoding          = "utf-8";
                    server.EncodingDetection = WebRequests.CharsetDetection.DefaultCharset;
                    string response          = server.Get (appUrl);

                    // Flag Indicating Success while processing and parsing this app
                    bool ProcessingWorked = true;

                    // Sanity Check
                    if (String.IsNullOrEmpty (response) || server.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        LogWriter.Info ("Error opening app page : " + appUrl);
                        ProcessingWorked = false;
                        
                        // Renewing WebRequest Object to get rid of Cookies
                        server = new WebRequests ();

                        // Inc. retry counter
                        retryCounter++;

                        Console.WriteLine ("Retrying:" + retryCounter);

                        // Checking for maximum retry count
                        double waitTime;
                        if (retryCounter >= 7)
                        {
                            waitTime = TimeSpan.FromMinutes (35).TotalMilliseconds;

                            // Removing App from the database (this the app page may have expired)
                            mongoDB.RemoveFromQueue (app.Url);

                            Process.Start ("PlayStoreWorker.exe");
                            Process.GetCurrentProcess ().Kill ();
                        }
                        else
                        {
                            // Calculating next wait time ( 2 ^ retryCounter seconds)
                            waitTime = TimeSpan.FromSeconds (Math.Pow (2, retryCounter)).TotalMilliseconds;
                        }

                        // Hiccup to avoid google blocking connections in case of heavy traffic from the same IP
                        Thread.Sleep (Convert.ToInt32 (waitTime));
                    }
                    else
                    {
                        // Reseting retry counter
                        retryCounter = 0;

                        // Parsing Useful App Data
                        AppModel parsedApp = parser.ParseAppPage (response, appUrl);

                        // Vu
                        // Here is where insert the app into the ProcessedApps Database.
                        // Attemp to check for the condition base on number of instalation and rating

                        // First split the string into the string array
                        string[] installations;
                        string[] separators = new string[] { " - " };
                        // Getting the Installation number for the current app
                        installations = parsedApp.Instalations.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                        installations[0] = installations[0].Replace(",", "");   // replace the "," in the number of installations
                        installations[1] = installations[1].Replace(",", "");
                        long install_num = 0;
                        try {
                            install_num = Convert.ToInt64(installations[0]);
                        }
                        catch (OverflowException) {
                            Console.WriteLine("{0} is outside the range of the Int64 type.");
                        }
                        catch (FormatException) {
                            Console.WriteLine("The {0} value '{1}' is not recognizable");
                        }
                        
                        bool removed = false;
                        // Getting the rating for the current app
                        double rating = parsedApp.Score.Total;

                        // Getting the developer name ( company name)
                        string developer = parsedApp.Developer;
                                               
                        // if the installation number is less than 1000,000 
                        // OR rating less than 3 stars
                        // OR appName is empty
                        // -> skip the app

                        string appName = parsedApp.Name;
                        if (install_num < 1000000 || rating < 3.5 || appName == "" || appName == null)
                        {
                            Console.WriteLine("Cannot add app <" + appName + "> -- NOT MEET CRITERIA");
                            // TODO: Update the NotMeetCriteria
                            // Removing App from the database
                            mongoDB.RemoveFromQueue(app.Url);
                            removed = true;
                        }
                        // Inserting App into MONGO_COLLECTION collection
                        // if the Insert func return false, then print a message indicates that
                        if (ProcessingWorked && !mongoDB.Insert<AppModel>(parsedApp) && !removed)
                        {
                            Console.WriteLine("Cannot add app <" + appName + "> -- FAIL TO ADD TO Database");
                            ProcessingWorked = false;
                        }

                        // If processing failed, do not remove the app from the database, instead, keep it and flag it as not busy 
                        // so that other workers can try to process it later
                        if (!ProcessingWorked)
                        {
                            mongoDB.ToggleBusyApp(app, false);
                        }
                        else // On the other hand, if processing worked, removes it from the database
                        {
                            // Console Feedback, Comment this line to disable if you want to
                            if (!removed)
                            {
                                Console.WriteLine("Inserted App : " + parsedApp.Name);
                                 mongoDB.RemoveFromQueue(app.Url);
                            }
                            else
                            {
                                Console.WriteLine("Removed App : " + parsedApp.Name);
                            }                           
                        }


                        // Vu
                        // TRY TO NOT DOWNLOAD THE RELATED APPS
                        /*
                        // Counters for console feedback only
                        int extraAppsCounter = 0, newExtraApps = 0;

                        // Parsing "Related Apps" and "More From Developer" Apps (URLS Only)
                        foreach (string extraAppUrl in parser.ParseExtraApps (response))
                        {
                            // Incrementing counter of extra apps
                            extraAppsCounter++;

                            // Assembling Full app Url to check with database
                            string fullExtraAppUrl = Consts.APP_URL_PREFIX + extraAppUrl;

                            // Checking if the app was either processed or queued to be processed already
                            if ((!mongoDB.AppProcessed (fullExtraAppUrl)) && (!mongoDB.IsAppOnQueue(extraAppUrl)))
                            {
                                // Incrementing counter of inserted apps
                                newExtraApps++;

                                // Adds it to the queue of apps to be processed
                                mongoDB.AddToQueue (extraAppUrl);
                            }
                        }

                        // Console Feedback
                        Console.WriteLine ("Queued " + newExtraApps + " / " + extraAppsCounter + " related apps");
                        
                        */

                        // Hiccup (used to minimize blocking issues)
                        Thread.Sleep (300);
                    }
                }
                catch (Exception ex)
                {
                    LogWriter.Error (ex);
                }
                finally
                {
                    try
                    {
                        // Toggles Busy status back to false
                        mongoDB.ToggleBusyApp(app, false);
                    }
                    catch (Exception ex)
                    {
                        // Toggle Busy App may raise an exception in case of lack of internet connection, so, i must use this
                        // "inner catch" to avoid it from happenning
                        LogWriter.Error (ex);
                    }
                }
            }
        }
    }
}
