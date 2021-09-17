using System;
using System.Web;

namespace Hi.UrlRewrite.Entities.Actions.Base
{
    public interface IBaseRedirect : IBaseRewriteUrl, IBaseAppendQueryString, IBaseCache, IBaseStopProcessing, IBaseStatusCode
    {
        new string Name { get; set; }
        new string RewriteUrl { get; set; }
        new Guid? RewriteItemId { get; set; }
        new string RewriteItemAnchor { get; set; }
        new bool AppendQueryString { get; set; }

        new HttpCacheability? HttpCacheability { get; set; }
        new bool StopProcessingOfSubsequentRules { get; set; }
        new RedirectStatusCode? StatusCode { get; set; }
    }
}
