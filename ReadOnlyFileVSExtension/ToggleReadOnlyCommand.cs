using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using Task = System.Threading.Tasks.Task;

namespace ReadOnlyFileVSExtension
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class ToggleReadOnlyCommand
    {
        public const int CommandId = 0x0100;

        public static readonly Guid CommandSet = new Guid("7593ab80-e247-452a-8c2d-274be3d5b7df");

        private static AsyncPackage _package;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToggleReadOnlyCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private ToggleReadOnlyCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(Execute, null, BeforeQueryStatus, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static ToggleReadOnlyCommand Instance { get; private set; }

        private static Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => _package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in ToggleReadOnlyCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            Instance = new ToggleReadOnlyCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(DTE)) is DTE2 dte2))
            {
                ShowMessage("Error: Could not get DTE service.");
                return;
            }

            var selectedItem = ((Array)dte2.ToolWindows.SolutionExplorer.SelectedItems).GetValue(0) as UIHierarchyItem;
            var projectItem = selectedItem?.Object as ProjectItem;

            var filePath = projectItem.FileNames[1];

            try
            {
                var openDoc = GetOpenDocument(dte2, filePath);
                if (openDoc?.Saved == false)
                {
                    openDoc.Save();
                }

                var attributes = File.GetAttributes(filePath);
                var isReadOnly = (attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;

                if (isReadOnly)
                {
                    attributes &= ~FileAttributes.ReadOnly;
                    File.SetAttributes(filePath, attributes);
                    ShowMessage($"The file '{Path.GetFileName(filePath)}' is now writable");
                }
                else
                {
                    attributes |= FileAttributes.ReadOnly;
                    File.SetAttributes(filePath, attributes);
                    ShowMessage($"The file '{Path.GetFileName(filePath)}' is now read-only");
                }

                if (openDoc != null)
                {
                    openDoc.Close(vsSaveChanges.vsSaveChangesYes);
                    dte2.ItemOperations.OpenFile(filePath);
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// This function finds the open document in Visual Studio by its file path.
        /// </summary>
        /// <param name="dte">An instance of the DTE2 object, which provides access to the Visual Studio automation model,
        /// allowing interaction with the IDE, such as accessing open documents or the Solution Explorer.</param>
        /// <param name="filePath">The full path of the file to search for in the currently open documents.</param>
        /// <returns>Returns the Document object if the file is open in Visual Studio; otherwise, returns null.</returns>
        private static Document GetOpenDocument(DTE2 dte, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (Document doc in dte.Documents)
            {
                if (doc.FullName.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return doc;
                }
            }

            return null;
        }

        /// <summary>
        /// This function is called when the command is queried for its status before showing.
        /// It updates the command text based on the current file's read-only status.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(sender is OleMenuCommand menuCommand))
                return;

            menuCommand.Text = "Toggle Read-Only Status";
            menuCommand.Visible = false;

            try
            {
                if (!(Package.GetGlobalService(typeof(DTE)) is DTE2 dte2))
                    return;

                var selectedItems = (Array)dte2.ToolWindows.SolutionExplorer.SelectedItems;
                if (selectedItems == null || selectedItems.Length != 1)
                    return;

                var selectedItem = selectedItems.GetValue(0) as UIHierarchyItem;
                if (!(selectedItem?.Object is ProjectItem projectItem))
                    return;

                var filePath = projectItem.FileNames[1];
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return;

                menuCommand.Visible = true;

                var attributes = File.GetAttributes(filePath);
                var isReadOnly = (attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;

                menuCommand.Text = isReadOnly ? "Mark as Writable" : "Mark as Read-Only";

            }
            catch
            {
                menuCommand.Visible = false;
            }
        }

        private static void ShowMessage(string message)
        {
            VsShellUtilities.ShowMessageBox(
                _package,
                message,
                "Toggle Read-Only Status",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
