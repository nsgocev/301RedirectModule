using Sitecore.Diagnostics;
using System.Web;

namespace SharedSource.RedirectModule.Rules.Conditions
{
    public class URLStringCondtion<T> : Sitecore.Rules.Conditions.StringOperatorCondition<T> where T : Sitecore.Rules.RuleContext
    {
        public string Value { get; set; }

        protected override bool Execute(T ruleContext)
        {
            Assert.ArgumentNotNull(ruleContext, "ruleContext");
            string str = Value;
            if (str == null)
            {
                return false;
            }

            return Compare(HttpContext.Current.Request.Url.ToString(), str);
        }
    }
}