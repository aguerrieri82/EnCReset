using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EnCReset
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("EnC Reset", "Resets stale Roslyn Edit and Continue tracking state", "1.0")]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 2)]
    [Guid(PackageGuidString)]
    public sealed class EnCResetPackage : AsyncPackage
    {
        public const string PackageGuidString = "8d42c449-f2ea-4d7a-9430-b005cc3dad98";
        public const string CommandSetGuidString = "9368c0dd-26d4-4dd3-a5cc-1f4fd47bdabb";
        public const int CommandId = 0x0100;
        public const int ToggleAutomaticResetCommandId = 0x0101;
        const string SettingsCollection = "EnCReset";
        const string AutomaticResetSetting = "AutomaticResetEnabled";

        WritableSettingsStore _settingsStore;
        bool _automaticResetEnabled = true;
        int _automaticResetVersion;

        DebuggerEvents _debuggerEvents;

        public EnCResetPackage()
        {

        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var settingsManager = new ShellSettingsManager(this);
            _settingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            if (_settingsStore.PropertyExists(SettingsCollection, AutomaticResetSetting))
                _automaticResetEnabled = _settingsStore.GetBoolean(SettingsCollection, AutomaticResetSetting);

            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            commandService?.AddCommand(new MenuCommand((s, e) => Reset(), new CommandID(new Guid(CommandSetGuidString), CommandId)));

            var automaticResetCommand = new OleMenuCommand(
                (s, e) => ToggleAutomaticReset(),
                new CommandID(new Guid(CommandSetGuidString), ToggleAutomaticResetCommandId));
            automaticResetCommand.BeforeQueryStatus += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                automaticResetCommand.Checked = _automaticResetEnabled;
            };
            commandService?.AddCommand(automaticResetCommand);

            var dte = await GetServiceAsync(typeof(SDTE)) as DTE;
            _debuggerEvents = dte?.Events.DebuggerEvents;

            if (_debuggerEvents != null)
                _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
        }

        void ToggleAutomaticReset()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var enabled = !_automaticResetEnabled;
            if (!_settingsStore.CollectionExists(SettingsCollection))
                _settingsStore.CreateCollection(SettingsCollection);
            _settingsStore.SetBoolean(SettingsCollection, AutomaticResetSetting, enabled);
            _automaticResetEnabled = enabled;
            _automaticResetVersion++;
        }

        void OnEnterDesignMode(dbgEventReason reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_automaticResetEnabled)
                return;

            var version = _automaticResetVersion;
            JoinableTaskFactory.RunAsync(async delegate
            {
                await Task.Delay(250);
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_automaticResetEnabled && version == _automaticResetVersion)
                    Reset();
            });
        }

        void Reset()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var componentModel = GetService(typeof(SComponentModel)) as IComponentModel;
                if (componentModel == null)
                    return;

                var workspaceType = FindType("Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace");
                var trackingServiceType = FindType("Microsoft.CodeAnalysis.EditAndContinue.IActiveStatementTrackingService");

                if (workspaceType == null || trackingServiceType == null)
                    return;

                var workspace = GetComponentService(componentModel, workspaceType);
                if (workspace == null)
                    return;

                var services = workspace.GetType().GetProperty("Services", BindingFlags.Instance | BindingFlags.Public)?.GetValue(workspace);
                if (services == null)
                    return;

                var trackingService = GetWorkspaceService(services, trackingServiceType);
                if (trackingService == null)
                    return;

                var session = trackingService.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
                if (session?.GetValue(trackingService) == null)
                    return;

                var endTracking = trackingService.GetType().GetMethod("EndTracking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (endTracking == null)
                    return;

                endTracking.Invoke(trackingService, null);
                WriteResetSuccess();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"EnC Reset: {ex.InnerException}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnC Reset: {ex}");
            }
        }

        void WriteResetSuccess()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var output = GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (output == null)
                return;

            var paneGuid = Microsoft.VisualStudio.VSConstants.GUID_OutWindowDebugPane;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(output.GetPane(ref paneGuid, out var pane));
            if (pane != null)
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(
                    pane.OutputString(ResetOutputClassifier.SuccessMessage + Environment.NewLine));
        }
        static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
        }

        static object GetComponentService(IComponentModel componentModel, Type serviceType)
        {
            var method = typeof(IComponentModel).GetMethods().First(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            return method.MakeGenericMethod(serviceType).Invoke(componentModel, null);
        }

        static object GetWorkspaceService(object services, Type serviceType)
        {
            var method = services.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

            return method?.MakeGenericMethod(serviceType).Invoke(services, null);
        }
    }
}
