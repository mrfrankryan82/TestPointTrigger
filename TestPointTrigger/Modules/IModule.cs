// TestPoint Trigger - Module contract
// Developer: HaKDMoDz™ · v2.1.0 · 2026-09-26
using System;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Contract every TestPoint Trigger module implements. The host
    /// discovers modules, shows them in its navigation, and owns their
    /// lifecycle. A module never assumes it is the only one loaded, and
    /// never blocks the UI thread in Activate/Deactivate.
    /// </summary>
    public interface IModule : IDisposable
    {
        /// <summary>Stable id, e.g. "livecoach". Used for settings keys.</summary>
        string Id { get; }

        /// <summary>Display name for the host's navigation.</summary>
        string Title { get; }

        /// <summary>Short line describing what the module does.</summary>
        string Description { get; }

        /// <summary>Module version, shown alongside the host credit.</summary>
        string Version { get; }

        /// <summary>Order hint for navigation; lower sorts first.</summary>
        int SortOrder { get; }

        /// <summary>
        /// Build (or return) the module's root control. Called once, lazily,
        /// the first time the module is shown. Must not start any timers,
        /// cameras or background work - that belongs in Activate.
        /// </summary>
        Control CreateView(IModuleHost host);

        /// <summary>Called when the module becomes the visible page.</summary>
        void Activate();

        /// <summary>
        /// Called when the module is navigated away from. Must stop cameras,
        /// timers and polling so background modules cost nothing.
        /// </summary>
        void Deactivate();
    }

    /// <summary>Services the host offers back to its modules.</summary>
    public interface IModuleHost
    {
        /// <summary>Write a line to the host status bar.</summary>
        void SetStatus(string text);

        /// <summary>Raise a transient notification to the operator.</summary>
        void Notify(string message, ModuleSeverity severity);

        /// <summary>Ask the host to switch to another module by id.</summary>
        bool Navigate(string moduleId);
    }

    public enum ModuleSeverity { Info, Success, Warning, Error }
}
