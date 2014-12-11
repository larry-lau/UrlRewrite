﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using Hi.UrlRewrite.Entities;
using Hi.UrlRewrite.Processing.Results;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Links;
using Sitecore.Resources.Media;

namespace Hi.UrlRewrite.Processing
{
    public class UrlRewriter
    {

        public ProcessRequestResult ProcessRequestUrl(Uri requestUri, List<InboundRule> inboundRules)
        {
            if (inboundRules == null)
            {
                throw new ArgumentNullException("inboundRules");
            }

            var originalUri = requestUri;

            Log.Debug(string.Format("UrlRewrite - Processing url: {0}", originalUri), this);

            var matchedAtLeastOneRule = false;

            var ruleResult = new RuleResult
            {
                RewrittenUri = originalUri
            };

            var processedResults = new List<RuleResult>();

            foreach (var inboundRule in inboundRules)
            {
                ruleResult = ProcessInboundRule(ruleResult.RewrittenUri, inboundRule);
                processedResults.Add(ruleResult);

                if (!ruleResult.RuleMatched)
                    continue;

                matchedAtLeastOneRule = true;

                if (ruleResult.RuleMatched && ruleResult.StopProcessing)
                {
                    ruleResult.StoppedProcessing = true;
                    break;
                }
            }

            Log.Debug(string.Format("UrlRewrite - Processed originalUrl: {0} redirectedUrl: {1}", originalUri, ruleResult.RewrittenUri), this);

            var finalResult = new ProcessRequestResult(originalUri, ruleResult, matchedAtLeastOneRule, processedResults);

            return finalResult;
        }

        public void ExecuteResult(HttpResponseBase httpResponse, ProcessRequestResult ruleResult)
        {
            try
            {
                httpResponse.Clear();

                if (!ruleResult.Abort && ruleResult.CustomResponse == null)
                {
                    httpResponse.RedirectLocation = ruleResult.RewrittenUri.ToString();
                    httpResponse.StatusCode = ruleResult.StatusCode ?? (int)HttpStatusCode.MovedPermanently;

                    if (ruleResult.HttpCacheability.HasValue)
                    {
                        httpResponse.Cache.SetCacheability(ruleResult.HttpCacheability.Value);
                    }
                }
                else if (ruleResult.Abort)
                {
                    // do nothing
                }
                else if (ruleResult.CustomResponse != null)
                {
                    httpResponse.TrySkipIisCustomErrors = true;

                    var customResponse = ruleResult.CustomResponse;

                    httpResponse.StatusCode = customResponse.StatusCode;
                    httpResponse.StatusDescription = customResponse.ErrorDescription;
                    // TODO: Implement Status Reason?
                    //httpResponse.??? = customResponse.Reason;

                    if (customResponse.SubStatusCode.HasValue)
                    {
                        httpResponse.SubStatusCode = customResponse.SubStatusCode.Value;
                    }

                }

                httpResponse.End();
            }
            catch (ThreadAbortException)
            {
                // swallow this exception because we may have called Response.End
            }
        }

        private RuleResult ProcessInboundRule(Uri originalUri, InboundRule inboundRule)
        {
            Log.Debug(string.Format("UrlRewrite - Processing inbound rule - requestUri: {0} inboundRule: {1}", originalUri, inboundRule.Name), this);

            var ruleResult = new RuleResult
            {
                OriginalUri = originalUri,
                RewrittenUri = originalUri
            };

            switch (inboundRule.Using)
            {
                case Using.ExactMatch:
                case Using.RegularExpressions:

                    ruleResult = ProcessRegularExpressionInboundRule(ruleResult.OriginalUri, inboundRule);

                    break;
                case Using.Wildcards:
                    //TODO: Implement Wildcards
                    throw new NotImplementedException("Using Wildcards has not been implemented");
                    break;

                default:
                    break;
            }

            Log.Debug(string.Format("UrlRewrite - Processing inbound rule - requestUri: {0} inboundRule: {1} rewrittenUrl: {2}", ruleResult.OriginalUri, inboundRule.Name, ruleResult.RewrittenUri), this);

            ruleResult.ItemId = inboundRule.ItemId;

            return ruleResult;
        }

        private RuleResult ProcessRegularExpressionInboundRule(Uri originalUri, InboundRule inboundRule)
        {

            var ruleResult = new RuleResult
            {
                OriginalUri = originalUri,
                RewrittenUri = originalUri
            };

            Match inboundRuleMatch;

            // test pattern matches
            bool isInboundRuleMatch = TestPatternMatches(inboundRule, originalUri, out inboundRuleMatch);

            // test conditions
            if (isInboundRuleMatch && inboundRule.Conditions != null && inboundRule.Conditions.Count > 0)
            {
                isInboundRuleMatch = TestConditions(inboundRule, originalUri);
            }

            // test site name restrictions
            if (isInboundRuleMatch && !string.IsNullOrEmpty(inboundRule.SiteNameRestriction))
            {
                isInboundRuleMatch = TestSiteNameRestriction(inboundRule);
            }

            if (isInboundRuleMatch && inboundRule.Action != null)
            {

                ruleResult.RuleMatched = true;

                Log.Debug(string.Format("UrlRewrite - INBOUND RULE MATCH - requestUri: {0} inboundRule: {1}", originalUri, inboundRule.Name), this);

                // TODO: Need to implement Rewrite, None, Custom Response

                if (inboundRule.Action is RedirectAction) // process the action if it is a RedirectAction  
                {
                    ProcessRedirectAction(inboundRule, originalUri, inboundRuleMatch, ruleResult);
                }
                else if (inboundRule.Action is AbortRequestAction)
                {
                    ProcessAbortRequestAction(inboundRule, ruleResult);
                }
                else if (inboundRule.Action is CustomResponseAction)
                {
                    ProcessCustomResponseAction(inboundRule, ruleResult);
                }
                else
                {
                    throw new NotImplementedException("Redirect Action and Abort Reqeust Action are the only supported type of redirects");
                }
            }
            else if (inboundRule.Action == null)
            {
                Log.Warn(string.Format("UrlRewrite - Inbound Rule has no Action set - inboundRule: {0} inboundRule ItemId: {1}", inboundRule.Name, inboundRule.ItemId), this);

                // we are going to skip this because we don't know what to do with it during processing
                ruleResult.RuleMatched = false;
            }


            return ruleResult;
        }

        private bool TestPatternMatches(InboundRule inboundRule, Uri originalUri, out Match inboundRuleMatch)
        {
            var isInboundRuleMatch = false;
            var absolutePath = originalUri.AbsolutePath;
            var uriPath = absolutePath.Substring(1); // remove starting "/"

            var escapedAbsolutePath = HttpUtility.UrlDecode(absolutePath);
            var escapedUriPath = (escapedAbsolutePath ?? string.Empty).Substring(1); // remove starting "/"

            // TODO : I have only implemented "MatchesThePattern" - need to implement the other types
            var matchesThePattern = inboundRule.RequestedUrl.HasValue &&
                                    inboundRule.RequestedUrl.Value == RequestedUrl.MatchesThePattern;

            if (!matchesThePattern)
            {
                throw new NotImplementedException(
                    "Have not yet implemented 'Does Not Match the Pattern' because of possible redirect loops");
            }

            var pattern = inboundRule.Pattern;

            if (inboundRule.Using.HasValue && inboundRule.Using.Value == Using.ExactMatch)
            {
                pattern = "^" + pattern + "$";
            }

            var inboundRuleRegex = new Regex(pattern, inboundRule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

            inboundRuleMatch = inboundRuleRegex.Match(uriPath);
            isInboundRuleMatch = matchesThePattern ? inboundRuleMatch.Success : !inboundRuleMatch.Success;

            Log.Debug(
                string.Format("UrlRewrite - Regex - Pattern: '{0}' Input: '{1}' Success: {2}", pattern, uriPath,
                    isInboundRuleMatch), this);

            if (!isInboundRuleMatch && !uriPath.Equals(escapedUriPath, StringComparison.InvariantCultureIgnoreCase))
            {
                inboundRuleMatch = inboundRuleRegex.Match(escapedUriPath);
                isInboundRuleMatch = matchesThePattern ? inboundRuleMatch.Success : !inboundRuleMatch.Success;

                Log.Debug(
                    string.Format("UrlRewrite - Regex - Pattern: '{0}' Input: '{1}' Success: {2}", pattern, escapedUriPath,
                        isInboundRuleMatch), this);
            }
            return isInboundRuleMatch;
        }

        private bool TestConditions(InboundRule inboundRule, Uri originalUri)
        {
            var conditionMatches = false;

            var conditionLogicalGrouping = inboundRule.ConditionLogicalGrouping.HasValue
                ? inboundRule.ConditionLogicalGrouping.Value
                : LogicalGrouping.MatchAll;

            if (conditionLogicalGrouping == LogicalGrouping.MatchAll)
            {
                conditionMatches = inboundRule.Conditions.All(condition => ConditionMatch(originalUri, condition));
            }
            else
            {
                conditionMatches = inboundRule.Conditions.Any(condition => ConditionMatch(originalUri, condition));
            }

            return conditionMatches;
        }

        private bool TestSiteNameRestriction(InboundRule inboundRule)
        {
            var currentSiteName = Sitecore.Context.Site.Name;
            bool isInboundRuleMatch = false;

            if (currentSiteName != null)
            {
                isInboundRuleMatch = currentSiteName.Equals(inboundRule.SiteNameRestriction,
                    StringComparison.InvariantCultureIgnoreCase);

                if (!isInboundRuleMatch)
                {
                    Log.Debug(
                        string.Format(
                            "UrlRewrite - Regex - Rule '{0}' failed.  Site '{1}' does not equal rules site condition '{2}'",
                            inboundRule.Name, currentSiteName, inboundRule.SiteNameRestriction), this);
                }
                else
                {
                    Log.Debug(
                        string.Format(
                            "UrlRewrite - Regex - Rule '{0}' matched site name restriction.  Site '{1}' equal rules site condition '{2}'",
                            inboundRule.Name, currentSiteName, inboundRule.SiteNameRestriction), this);
                }
            }
            else
            {
                Log.Warn(
                    string.Format(
                        "UrlRewrite - Regex - Rule '{0}' matching based on site name will not occur because site name is null.",
                        inboundRule.Name), this);
            }

            return isInboundRuleMatch;
        }

        private void ProcessCustomResponseAction(InboundRule inboundRule, RuleResult ruleResult)
        {
            var customResponseAction = inboundRule.Action as CustomResponseAction;

            ruleResult.CustomResponse = customResponseAction;
            ruleResult.StopProcessing = true;
        }

        private void ProcessAbortRequestAction(InboundRule inboundRule, RuleResult ruleResult)
        {
            var abortRequestAction = inboundRule.Action as AbortRequestAction;

            ruleResult.Abort = true;
            ruleResult.StopProcessing = true;
        }

        private void ProcessRedirectAction(InboundRule inboundRule, Uri uri, Match inboundRuleMatch,
            RuleResult ruleResult)
        {
            var redirectAction = inboundRule.Action as RedirectAction;

            var rewriteUrl = redirectAction.RewriteUrl;
            var rewriteItemId = redirectAction.RewriteItemId;
            var rewriteItemAnchor = redirectAction.RewriteItemAnchor;

            if (rewriteItemId.HasValue)
            {
                rewriteUrl = GetRewriteUrlFromItemId(rewriteItemId.Value, rewriteItemAnchor);
            }

            // process token replacements

            // replace host
            rewriteUrl = rewriteUrl.Replace("{HTTP_HOST}", uri.Host);

            if (redirectAction.AppendQueryString)
            {
                rewriteUrl += uri.Query;
            }

            // process capture groups
            var ruleCaptureGroupRegex = new Regex(@"({R:(\d+)})", RegexOptions.None);

            foreach (Match ruleCaptureGroupMatch in ruleCaptureGroupRegex.Matches(rewriteUrl))
            {
                var num = ruleCaptureGroupMatch.Groups[2];
                var groupIndex = Convert.ToInt32(num.Value);
                var group = inboundRuleMatch.Groups[groupIndex];
                var matchText = ruleCaptureGroupMatch.ToString();

                rewriteUrl = rewriteUrl.Replace(matchText, @group.Value);
            }

            var redirectType = redirectAction.RedirectType;

            // get the status code
            ruleResult.StatusCode = redirectType.HasValue ? (int)redirectType : (int)HttpStatusCode.MovedPermanently;

            ruleResult.RewrittenUri = new Uri(rewriteUrl);
            ruleResult.StopProcessing = redirectAction.StopProcessingOfSubsequentRules;
            ruleResult.HttpCacheability = redirectAction.HttpCacheability;
        }

        private string GetRewriteUrlFromItemId(Guid rewriteItemId, string rewriteItemAnchor)
        {
            string rewriteUrl = null;

            var db = Sitecore.Context.Database;
            if (db != null)
            {
                var rewriteItem = db.GetItem(new ID(rewriteItemId));

                if (rewriteItem != null)
                {
                    if (rewriteItem.Paths.IsMediaItem)
                    {
                        var mediaUrlOptions = new MediaUrlOptions
                        {
                            AlwaysIncludeServerUrl = true
                        };

                        rewriteUrl = MediaManager.GetMediaUrl(rewriteItem, mediaUrlOptions);
                    }
                    else
                    {
                        var urlOptions = LinkManager.GetDefaultUrlOptions();
                        urlOptions.AlwaysIncludeServerUrl = true;
                        urlOptions.SiteResolving = true;

                        rewriteUrl = LinkManager.GetItemUrl(rewriteItem, urlOptions);
                    }

                    if (!string.IsNullOrEmpty(rewriteItemAnchor))
                    {
                        rewriteUrl += string.Format("#{0}", rewriteItemAnchor);
                    }
                }
            }

            return rewriteUrl;
        }

        private bool ConditionMatch(Uri uri, Condition condition)
        {
            var conditionRegex = new Regex(condition.Pattern, condition.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

            bool isMatch = false;

            if (condition.CheckIfInputString.HasValue)
            {
                switch (condition.CheckIfInputString.Value)
                {
                    case CheckIfInputStringType.MatchesThePattern:
                    case CheckIfInputStringType.DoesNotMatchThePattern:
                        switch (condition.ConditionInput)
                        {
                            case Hi.UrlRewrite.Entities.ConditionInputType.HTTP_HOST:
                                isMatch = conditionRegex.IsMatch(uri.Host);
                                break;
                            case Hi.UrlRewrite.Entities.ConditionInputType.QUERY_STRING:
                                isMatch = conditionRegex.IsMatch(uri.Query);
                                break;
                            case Hi.UrlRewrite.Entities.ConditionInputType.HTTPS:

                                var https = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.InvariantCultureIgnoreCase) ? "on" : "off"; //

                                isMatch = conditionRegex.IsMatch(https);
                                break;
                            default:
                                break;
                        }

                        if (condition.CheckIfInputString.Value == CheckIfInputStringType.DoesNotMatchThePattern)
                        {
                            isMatch = !isMatch;
                        }

                        break;
                    default:
                        throw new NotImplementedException("Only 'Matches the Pattern' and 'Does Not Match the Pattern' have been implemented.");
                        break;
                }

            }

            return isMatch;
        }

    }
}