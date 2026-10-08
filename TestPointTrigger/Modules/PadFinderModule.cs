// TestPoint Trigger - Pad Finder module
// Developer: HaKDMoDz™ · v3.0.0 · 2026-09-26
using System;
using System.Windows.Forms;

namespace TestPointTrigger.Modules
{
    /// <summary>
    /// Wraps the existing MainForm pad-finder UI as a module. The form is
    /// hosted as an MDI-less child control so none of its detection code
    /// needed changing; it simply lives inside the shell instead of being
    /// the application window.
    /// </summary>
    public class PadFinderModule : IModule
    {
        public string Id => "padfinder";
        public string Title => "PCB Pad Finder";
        public string Description => "Find and label test pads on motherboard photos";
        public string Version => "3.1.0";
        public int SortOrder => 20;

        private MainForm _form;
        private Panel _container;

        public Control CreateView(IModuleHost host)
        {
            _container = new Panel { Dock = DockStyle.Fill };

            _form = new MainForm
            {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Dock = DockStyle.Fill,
                ControlBox = false
            };

            _container.Controls.Add(_form);
            _form.Show();
            return _container;
        }

        public void Activate() { }

        public void Deactivate() { }

        public void Dispose()
        {
            if (_form != null && !_form.IsDisposed) _form.Dispose();
            _form = null;
            _container?.Dispose();
            _container = null;
        }
    }
}
