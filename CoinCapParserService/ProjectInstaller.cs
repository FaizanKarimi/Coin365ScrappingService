using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace CoinCapParserService
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            this.BeforeInstall += new InstallEventHandler(ProjectInstaller_BeforeInstall);
            InitializeComponent();
        }
        void ProjectInstaller_BeforeInstall(object sender, InstallEventArgs e)
        {
            List<ServiceController> services = new List<ServiceController>(ServiceController.GetServices());

            foreach (ServiceController s in services)
            {
                if (s.ServiceName == this.CoinCapParser.ServiceName)
                {
                    ServiceInstaller ServiceInstallerObj = new ServiceInstaller();
                    ServiceInstallerObj.Context = new InstallContext();
                    ServiceInstallerObj.Context = Context;
                    ServiceInstallerObj.ServiceName = "Service1";
                    ServiceInstallerObj.Uninstall(null);

                    break;
                }
            }
        }
    }
}
