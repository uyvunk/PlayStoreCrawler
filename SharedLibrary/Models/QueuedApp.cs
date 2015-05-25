using System;
using System.Linq;
using MongoDB.Bson;

namespace SharedLibrary.Models
{
    public class QueuedApp
    {
        public ObjectId _id { get; set; }   // app id in QueuedApps DB
        public String Url   { get; set; }   // URL of the app
        public String Key { get; set; }     // The input key word that used to crawl this app
        public bool IsBusy { get; set; }   // Is the app being processed by a worker?
        public bool NotMeetCrit { get; set; }    // Has been checked and not meet the Criteria
    }
}
