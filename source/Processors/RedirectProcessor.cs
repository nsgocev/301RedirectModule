using System.Web;
using Sitecore.Data;
using System;
using Sitecore.Data.Items;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using Sitecore.Links;
using Sitecore.Pipelines.HttpRequest;
using Sitecore.Diagnostics;
using Sitecore.Resources.Media;
using SharedSource.RedirectModule.Classes;
using SharedSource.RedirectModule.Helpers;
using Sitecore.Data.Fields;
using Sitecore.Rules;
using Sitecore.Rules.Actions;

namespace SharedSource.RedirectModule.Processors
{
    /// <summary>
    ///  Redirection Module which handles redirects.  Both exact matches and regular expression pattern matches are supported.
    /// </summary>
    public class RedirectProcessor : HttpRequestProcessor
    {
        /// <summary>
        ///  The main method for the processor.  It simply overrides the Process method.
        /// </summary>
        public override void Process(HttpRequestArgs args)
        {
            // This processer is added to the pipeline after the Sitecore Item Resolver.  We want to skip everything if the item resolved successfully.
            // Also, skip processing for the visitor identification items related to DMS.
            Assert.ArgumentNotNull(args, "args");
            if ((Sitecore.Context.Item == null || AllowRedirectsOnFoundItem(Sitecore.Context.Database)) && args.LocalPath != Constants.Paths.VisitorIdentification && Sitecore.Context.Database != null)
            {
                // Grab the actual requested path for use in both the item and pattern match sections.
                string requestedUrl = HttpContext.Current.Request.Url.ToString();
                string requestedPath = HttpContext.Current.Request.Url.AbsolutePath;
                string requestedPathAndQuery = HttpContext.Current.Request.Url.PathAndQuery;
                Database db = Sitecore.Context.Database;

                // First, we check for exact matches because those take priority over pattern matches.
                if (Sitecore.Configuration.Settings.GetBoolSetting(Constants.Settings.RedirExactMatch, true))
                {
                    CheckForDirectMatch(db, requestedUrl, requestedPath, args);                    
                }

                // Next, we check for pattern matches because we didn't hit on an exact match.
                if (Sitecore.Configuration.Settings.GetBoolSetting(Constants.Settings.RedirPatternMatch, true))
                {
                    CheckForRegExMatch(db, requestedUrl, requestedPathAndQuery, args);
                }

                // Next, we check for rule matches because we didn't hit on an exact match or pattern match.
                if (Sitecore.Configuration.Settings.GetBoolSetting(Constants.Settings.RedirRuleMatch, true))
                {
                    CheckForRulesMatch(db, requestedUrl, args);
                }
            }
        }

        private void CheckForDirectMatch(Database db, string requestedUrl, string requestedPath, HttpRequestArgs args)
        {
            // Loop through the exact match entries to look for a match.
            foreach (Item possibleRedirect in GetRedirects(db, Constants.Templates.RedirectUrl, Constants.Templates.VersionedRedirectUrl, Sitecore.Configuration.Settings.GetSetting(Constants.Settings.QueryExactMatch)))
            {
                if (requestedUrl.Equals(possibleRedirect[Constants.Fields.RequestedUrl], StringComparison.OrdinalIgnoreCase) ||
                     requestedPath.Equals(possibleRedirect[Constants.Fields.RequestedUrl], StringComparison.OrdinalIgnoreCase))
                {
                    Field redirectToItemId = possibleRedirect.Fields[Constants.Fields.RedirectToItem];
                    Field redirectToUrl = possibleRedirect.Fields[Constants.Fields.RedirectToUrl];

                    if (redirectToItemId.HasValue && !string.IsNullOrEmpty(redirectToItemId.ToString()))
                    {
                        Item redirectToItem = db.GetItem(ID.Parse(redirectToItemId));

                        if (redirectToItem != null)
                        {
                            ResponseStatus responseStatus = GetResponseStatus(possibleRedirect);

                            SendResponse(redirectToItem, HttpContext.Current.Request.Url.Query, responseStatus, args);
                        }
                    }
                    else if (redirectToUrl.HasValue && !string.IsNullOrEmpty(redirectToUrl.ToString()))
                    {
                        ResponseStatus responseStatus = GetResponseStatus(possibleRedirect);

                        SendResponse(redirectToUrl.Value, HttpContext.Current.Request.Url.Query, responseStatus, args);
                    }
                }
            }
        }

        private void CheckForRegExMatch(Database db, string requestedUrl, string requestedPathAndQuery, HttpRequestArgs args)
        {
            // Loop through the pattern match items to find a match
            foreach (Item possibleRedirectPattern in GetRedirects(db, Constants.Templates.RedirectPattern, Constants.Templates.VersionedRedirectPattern, Sitecore.Configuration.Settings.GetSetting(Constants.Settings.QueryExactMatch)))
            {
                string redirectPath = string.Empty;
                if (Regex.IsMatch(requestedUrl, possibleRedirectPattern[Constants.Fields.RequestedExpression], RegexOptions.IgnoreCase))
                {
                    redirectPath = Regex.Replace(requestedUrl, possibleRedirectPattern[Constants.Fields.RequestedExpression],
                                                 possibleRedirectPattern[Constants.Fields.SourceItem], RegexOptions.IgnoreCase);
                }
                else if (Regex.IsMatch(requestedPathAndQuery, possibleRedirectPattern[Constants.Fields.RequestedExpression], RegexOptions.IgnoreCase))
                {
                    redirectPath = Regex.Replace(requestedPathAndQuery,
                                                 possibleRedirectPattern[Constants.Fields.RequestedExpression],
                                                 possibleRedirectPattern[Constants.Fields.SourceItem], RegexOptions.IgnoreCase);
                }

                if (string.IsNullOrEmpty(redirectPath))
                {
                    continue;
                }

                // Query portion gets in the way of getting the sitecore item.
                string[] pathAndQuery = redirectPath.Split('?');
                string path = pathAndQuery[0];

                if (LinkManager.GetDefaultUrlOptions() != null &&
                    LinkManager.GetDefaultUrlOptions().EncodeNames)
                {
                    path = Sitecore.MainUtil.DecodeName(path);
                }
                Item redirectToItem = db.GetItem(path);
                if (redirectToItem != null)
                {
                    string query = pathAndQuery.Length > 1 ? "?" + pathAndQuery[1] : "";
                    ResponseStatus responseStatus = GetResponseStatus(possibleRedirectPattern);

                    SendResponse(redirectToItem, query, responseStatus, args);
                }
            }
        }

        private void CheckForRulesMatch(Database db, string requestedUrl, HttpRequestArgs args)
        {
            // Loop through the pattern match items to find a match
            foreach (Item possibleRedirectRule in GetRedirects(db, Constants.Templates.RedirectRule, Constants.Templates.VersionedRedirectRule, Sitecore.Configuration.Settings.GetSetting(Constants.Settings.QueryExactMatch)))
            {
                RuleContext ruleContext = new RuleContext();
                ruleContext.Parameters.Add("newUrl", requestedUrl);

                foreach (Rule<RuleContext> rule in RuleFactory.GetRules<RuleContext>(new [] { possibleRedirectRule }, "Redirect Rule").Rules)
                {
                    if (rule.Condition != null)
                    {
                        RuleStack stack = new RuleStack();
                        rule.Condition.Evaluate(ruleContext, stack);
                        if (!ruleContext.IsAborted && (stack.Count != 0 && (bool)stack.Pop()))
                        {
                            foreach (RuleAction<RuleContext> action in rule.Actions)
                            {
                                action.Apply(ruleContext);
                            }
                        }
                    }   
                }

                if (ruleContext.Parameters["newUrl"] != null && ruleContext.Parameters["newUrl"].ToString() != string.Empty && ruleContext.Parameters["newUrl"].ToString() != requestedUrl)
                {
                   ResponseStatus responseStatus = GetResponseStatus(possibleRedirectRule);
                    // The query string will be in the URL already, so don't break it apart.
                    SendResponse(ruleContext.Parameters["newUrl"].ToString(), string.Empty, responseStatus, args);
                }
            }
        }

        private static bool AllowRedirectsOnFoundItem(Database db)
        {
            if (db == null)
            {
                return false;
            }

            string redirectRoot = Sitecore.Configuration.Settings.GetSetting(Constants.Settings.RedirectRootNode);
            Item redirectFolderRoot = db.SelectSingleItem(redirectRoot);
            if (redirectFolderRoot == null)
            {
                return false;
            }

            string allowRedirectsOnItemIDs = redirectFolderRoot[Constants.Fields.ItemProcessRedirects];
            return allowRedirectsOnItemIDs != null &&
                      allowRedirectsOnItemIDs.Contains(Sitecore.Context.Item.ID.ToString());
        }

        /// <summary>
        ///  This method return all of the possible matches for either the exact matches or the pattern matches
        ///  Note: Because Fast Query does not guarantee to return items in the current language context
        ///  (e.g. while in US/English, results may include other language items as well, even if the 
        ///  US/EN language has no active versions), an additional LINQ query has to be run to filter for language.
        ///  Choose your query type appropriately.
        /// </summary>
        private static IEnumerable<Item> GetRedirects(Database db, string templateName, string versionedTemplateName, string queryType)
        {
            // Based off the config file, we can run different types of queries. 
            IEnumerable<Item> ret = null;
            string redirectRoot = Sitecore.Configuration.Settings.GetSetting(Constants.Settings.RedirectRootNode);
            switch (queryType)
            {
                case "fast": // fast query
                    {
                        //process shared template items
                        ret = db.SelectItems(string.Format("fast:{0}//*[@@templatename='{1}']", redirectRoot, templateName));

                        //because fast query requires to check for active versions in the current language
                        //run a separate query for versioned items to see if this is even necessary.
                        //if only shared templates exist in System/Modules, this step is extraneous and unnecessary.
                        IEnumerable<Item> versionedItems = db.SelectItems(string.Format("fast:{0}//*[@@templatename='{1}']", redirectRoot, versionedTemplateName));

                        //if active versions of items in the current context exist, union the two IEnumerable lists together.
                        ret = versionedItems.Any(i => i.Versions.Count > 0)
                            ? ret.Union(versionedItems.Where(i => i.Versions.Count > 0))
                            : ret;
                        break;
                    }
                case "query": // Sitecore query
                    {
                        ret = db.SelectItems(string.Format("{0}//*[@@templatename='{1}' or @@templatename='{2}']", redirectRoot, templateName, versionedTemplateName));
                        break;
                    }
                default: // API LINQ
                    {
                        Item redirectFolderRoot = db.SelectSingleItem(redirectRoot);
                        if (redirectFolderRoot != null)
                        {
                            ret = redirectFolderRoot.Axes.GetDescendants().Where(i => i.TemplateName == templateName || i.TemplateName == versionedTemplateName);
                        }

                        break;
                    }
            }

            // make sure to return an empty list instead of null
            return ret ?? new Item[0];
        }
                
        /// <summary>
        ///  Once a match is found and we have a Sitecore Item, we can send the response.
        /// </summary>
        private static void SendResponse(Item redirectToItem, string queryString, ResponseStatus responseStatus, HttpRequestArgs args)
        {
            string redirectToUrl = GetRedirectToItemUrl(redirectToItem);
            SendResponse(redirectToUrl, queryString, responseStatus, args);
        }

        private static void SendResponse(string redirectToUrl, string queryString, ResponseStatus responseStatusCode, HttpRequestArgs args)
        {
            args.HttpContext.Response.Status = responseStatusCode.Status;
            args.HttpContext.Response.StatusCode = responseStatusCode.StatusCode;
            args.HttpContext.Response.AddHeader("Location", redirectToUrl + queryString);
            args.HttpContext.Response.End();
        }

        private static string GetRedirectToItemUrl(Item redirectToItem)
        {
            if (redirectToItem.Paths.Path.StartsWith(Constants.Paths.MediaLibrary))
            {
                MediaItem mediaItem = redirectToItem;
                string mediaUrl = MediaManager.GetMediaUrl(mediaItem);
                string redirectToUrl = Sitecore.StringUtil.EnsurePrefix('/', mediaUrl);
                return redirectToUrl;
            }

            return LinkManager.GetItemUrl(redirectToItem);
        }

        private static ResponseStatus GetResponseStatus(Item redirectItem)
        {
            ResponseStatus result = new ResponseStatus
            {
                Status = "301 Moved Permanently",
                StatusCode = 301,
            };

            if (redirectItem != null)
            {
                Field responseStatusCodeId = redirectItem.Fields[Constants.Fields.ResponseStatusCode];

                if (responseStatusCodeId != null && responseStatusCodeId.HasValue && !string.IsNullOrEmpty(responseStatusCodeId.ToString()))
                {      
                    Item responseStatusCodeItem = redirectItem.Database.GetItem(ID.Parse(responseStatusCodeId));

                    if (responseStatusCodeItem != null)
                    {
                        result.Status = responseStatusCodeItem.Name;
                        result.StatusCode = responseStatusCodeItem.GetIntegerFieldValue(Constants.Fields.StatusCode, result.StatusCode);
                    }
                }
            }

            return result;
        }
    }
}

