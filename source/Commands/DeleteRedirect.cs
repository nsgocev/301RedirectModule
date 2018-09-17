using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Shell.Framework.Commands;
using Sitecore.Web.UI.Sheer;

namespace SharedSource.RedirectModule.Commands
{
    internal class DeleteRedirect : Command
    {
        public override void Execute(CommandContext context)
        {
            // execute the delete
            if (context.Items.Length == 0)
            {
                SheerResponse.Alert("The selected item could not be found.\n\nIt may have been deleted by another user.\n\nSelect another item.");
            }
            else
            {
                if (context.Items.Length == 1)
                {
                    Database.GetDatabase("master").GetItem(context.Items[0].ID).Delete();
                }
            }

            // force the item to refresh
            Item myItem = Database.GetDatabase("master").GetItem(new ID(context.Parameters[3]));
            if (myItem != null)
            {
                string load = string.Concat("item:load(id=", myItem.ID, ",language=", myItem.Language, ",version=", myItem.Version, ")");
                Sitecore.Context.ClientPage.SendMessage(this, load);
            }
        }
    }
}
