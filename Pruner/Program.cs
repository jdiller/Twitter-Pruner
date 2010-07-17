using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using TweetSharp;
using TweetSharp.Twitter.Extensions;
using TweetSharp.Twitter.Fluent;
using TweetSharp.Twitter.Model;

namespace Pruner
{
    class Program
    {
      
        private static IEnumerable<TwitterUser> _friends;

        //fill these in as appropriate
        //TODO: use OAuth/XAuth
        private static string _userName;
        private static string _password;

        static void Main(string[] args)
        {
            if ( args.Length < 2 )
            {
                Console.WriteLine("Usage: {0} username password", Process.GetCurrentProcess().ProcessName);
                return;
            }
            _userName = args[0];
            _password = args[1];

            Console.Write("Getting friends (takes a while)");
            _friends = GetFriends(() => Console.Write('.'));
            Console.WriteLine();
            
            //group friends by last post month and year
            var groups = from f in _friends
                         where f.Status != null
                         group f by new {Year = f.Status.CreatedDate.Year, Month = f.Status.CreatedDate.Month }
                             into months
                             select new { Year = months.Key.Year, Month = months.Key.Month, Count = months.Count() };

            //dump it to the console
            foreach (var g in groups)
            {
                Console.WriteLine("Month: {0}/{1}\tCount:{2}", g.Year, g.Month, g.Count);
            }

            //dump count of friends who have never posted 
            var neverPostedCount = _friends.Where(f => f.Status == null).Count();
            Console.WriteLine("Never:\tCount:{0}", neverPostedCount);

            bool proceed = false;
            DateTime date = DateTime.MinValue;
            var dateCheck = new Regex(@"^(19|20)?\d\d[-/.](0[1-9]|1[012])[-/.](0[1-9]|1[1-9]|2[1-9]|3[01])$");
            while( !proceed )
            {
                Console.Write("Prune friends whose last status is older than (YYYY/MM/DD):");
                var input = Console.ReadLine();
                if ( input != null && dateCheck.Match(input).Success)
                {
                    proceed = DateTime.TryParse(input, out date);
                }
            }
            
            //get the friends who haven't posted since the cutoff 
            var oldones = from f in _friends
                          where f.Status != null && f.Status.CreatedDate < date
                          orderby f.Status.CreatedDate ascending 
                          select f;
            
            //add the ones that have never posted ever 
            oldones.Union(_friends.Where(f => f.Status == null));

            //loop over the result and optionally unfollow
            bool all = false;
            foreach (var friend in oldones)
            {
                Console.WriteLine("\n{0} (@{1})\nPosts:{2}\nProtected:{3}\nLast Post:{4}", 
                    friend.Name, 
                    friend.ScreenName, 
                    friend.StatusesCount, 
                    friend.IsProtected, 
                    friend.Status == null ? "never" : friend.Status.CreatedDate.ToShortDateString());
                
                bool unfollow = all;
                if (!all)
                {
                    Console.WriteLine("Unfollow {0}? (Yes/No/All):", friend.ScreenName);
                    var key = Console.ReadKey(true).KeyChar.ToString().ToLowerInvariant();
                    all = key == "a";
                    unfollow = all || key == "y";
                }
                if (unfollow)
                {
                    Unfollow(friend);
                }
            }
            Console.WriteLine("All done");
            Console.ReadKey(true);

        }

        public static TwitterResult Unfollow(TwitterUser loser)
        {
            var unfollow = FluentTwitter.CreateRequest()
                .AuthenticateAs(_userName, _password)
                .Configuration.UseGzipCompression()
                .Friendships().Destroy(loser.Id);
            return unfollow.Request();
        }


        public static IEnumerable<TwitterUser> GetFriends(Action pageCallback)
        {

            if (_friends == null)
            {
                var twitter = FluentTwitter.CreateRequest()
                            .AuthenticateAs(_userName, _password)
                            .Configuration.UseGzipCompression()
                            .Users().GetFriends().For(_userName)
                            .CreateCursor()
                            .AsJson();
                _friends = GetAllCursorValues(twitter, s => s.AsUsers(), pageCallback);
            }
            return _friends;
        }


        private static IEnumerable<T> GetAllCursorValues<T>(ITwitterLeafNode twitter, Func<TwitterResult, IEnumerable<T>> conversionMethod, Action pageCallback)
        {
            long? nextCursor = -1;
            var ret = new List<T>();
            do
            {
                if (pageCallback != null)
                {
                    pageCallback();
                }
                twitter.Root.Parameters.Cursor = nextCursor;
                var response = twitter.Request();
                IEnumerable<T> values = conversionMethod(response);
                if (values != null)
                {
                    ret.AddRange(values);
                }
                nextCursor = response.AsNextCursor();
            } while (nextCursor.HasValue && nextCursor.Value != 0);
            return ret;
        }

    }
}