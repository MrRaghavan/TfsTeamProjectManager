using Microsoft.Practices.Prism.Events;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using TeamProjectManager.Common.Events;
using TeamProjectManager.Common.Infrastructure;
using TeamProjectManager.Common.ObjectModel;

namespace TeamProjectManager.Modules.Security
{
    [Export]
    public class SecurityViewModel : ViewModelBase
    {
        #region Properties

        public RelayCommand GetSecurityGroupsCommand { get; private set; }
        public RelayCommand DeleteSelectedSecurityGroupsCommand { get; private set; }
        public RelayCommand ExportSelectedSecurityGroupPermissionsCommand { get; private set; }
        public RelayCommand AddOrUpdateSecurityGroupCommand { get; private set; }
        public RelayCommand ResetSecurityPermissionsCommand { get; private set; }
        public RelayCommand LoadSecurityPermissionsCommand { get; private set; }
        public RelayCommand SaveSecurityPermissionsCommand { get; private set; }

        #endregion

        #region Observable Properties

        public MembershipQuery MembershipMode
        {
            get { return this.GetValue(MembershipModeProperty); }
            set { this.SetValue(MembershipModeProperty, value); }
        }

        public static ObservableProperty<MembershipQuery> MembershipModeProperty = new ObservableProperty<MembershipQuery, SecurityViewModel>(o => o.MembershipMode);

        public ICollection<SecurityGroupInfo> SecurityGroups
        {
            get { return this.GetValue(SecurityGroupsProperty); }
            set { this.SetValue(SecurityGroupsProperty, value); }
        }

        public static ObservableProperty<ICollection<SecurityGroupInfo>> SecurityGroupsProperty = new ObservableProperty<ICollection<SecurityGroupInfo>, SecurityViewModel>(o => o.SecurityGroups);

        public ICollection<SecurityGroupInfo> SelectedSecurityGroups
        {
            get { return this.GetValue(SelectedSecurityGroupsProperty); }
            set { this.SetValue(SelectedSecurityGroupsProperty, value); }
        }

        public static ObservableProperty<ICollection<SecurityGroupInfo>> SelectedSecurityGroupsProperty = new ObservableProperty<ICollection<SecurityGroupInfo>, SecurityViewModel>(o => o.SelectedSecurityGroups);

        public SecurityGroupChange SecurityGroupChange
        {
            get { return this.GetValue(SecurityGroupChangeProperty); }
            set { this.SetValue(SecurityGroupChangeProperty, value); }
        }

        public static readonly ObservableProperty<SecurityGroupChange> SecurityGroupChangeProperty = new ObservableProperty<SecurityGroupChange, SecurityViewModel>(o => o.SecurityGroupChange);

        #endregion

        #region Constructors

        [ImportingConstructor]
        public SecurityViewModel(IEventAggregator eventAggregator, ILogger logger)
            : base(eventAggregator, logger, "Security", "Allows you to manage the security for Team Projects.")
        {
            this.GetSecurityGroupsCommand = new RelayCommand(GetSecurityGroups, CanGetSecurityGroups);
            this.DeleteSelectedSecurityGroupsCommand = new RelayCommand(DeleteSelectedSecurityGroups, CanDeleteSelectedSecurityGroups);
            this.ExportSelectedSecurityGroupPermissionsCommand = new RelayCommand(ExportSelectedSecurityGroupPermissions, CanExportSelectedSecurityGroupPermissions);
            this.AddOrUpdateSecurityGroupCommand = new RelayCommand(AddOrUpdateSecurityGroup, CanAddOrUpdateSecurityGroup);
            this.ResetSecurityPermissionsCommand = new RelayCommand(ResetSecurityPermissions, CanResetSecurityPermissions);
            this.LoadSecurityPermissionsCommand = new RelayCommand(LoadSecurityPermissions, CanLoadSecurityPermissions);
            this.SaveSecurityPermissionsCommand = new RelayCommand(SaveSecurityPermissions, CanSaveSecurityPermissions);
            this.SecurityGroupChange = new SecurityGroupChange();
        }

        #endregion

        #region OnSelectedTeamProjectCollectionChanged

        protected override void OnSelectedTeamProjectCollectionChanged()
        {
            var tfs = this.GetSelectedTfsTeamProjectCollection();
            if (tfs == null)
            {
                this.SecurityGroupChange.SetPermissionGroups(null);
            }
            else
            {
                var permissionGroups = SecurityManager.GetPermissionGroups(tfs);
                this.SecurityGroupChange.SetPermissionGroups(permissionGroups);
            }
        }

        #endregion

        #region GetSecurityGroups Command

        private bool CanGetSecurityGroups(object argument)
        {
            return IsAnyTeamProjectSelected();
        }

        private void GetSecurityGroups(object argument)
        {
            var teamProjects = this.SelectedTeamProjects.ToList();
            var membershipMode = this.MembershipMode;
            var task = new ApplicationTask("Retrieving security groups", teamProjects.Count, true);
            PublishStatus(new StatusEventArgs(task));
            var worker = new BackgroundWorker();
            worker.DoWork += (sender, e) =>
            {
                var tfs = GetSelectedTfsTeamProjectCollection();
                var ims = tfs.GetService<IIdentityManagementService>();

                var step = 0;
                var securityGroups = new List<SecurityGroupInfo>();
                foreach (var teamProject in teamProjects)
                {
                    task.SetProgress(step++, string.Format(CultureInfo.CurrentCulture, "Processing Team Project \"{0}\"", teamProject.Name));
                    try
                    {
                        foreach (var applicationGroup in ims.ListApplicationGroups(teamProject.Uri.ToString(), ReadIdentityOptions.None).Where(g => g.IsActive).OrderBy(g => g.DisplayName))
                        {
                            var members = new List<string>();
                            if (membershipMode != MembershipQuery.None)
                            {
                                var applicationGroupWithMembers = ims.ReadIdentity(applicationGroup.Descriptor, membershipMode, ReadIdentityOptions.None);
                                if (applicationGroupWithMembers.Members != null && applicationGroupWithMembers.Members.Any())
                                {
                                    members.AddRange(ims.ReadIdentities(applicationGroupWithMembers.Members, membershipMode, ReadIdentityOptions.None).Where(m => m != null).Select(m => m.DisplayName));
                                }
                            }
                            var description = applicationGroup.GetAttribute("Description", null);
                            var securityGroup = new SecurityGroupInfo(teamProject, applicationGroup.Descriptor.Identifier, applicationGroup.DisplayName, description, members);
                            securityGroups.Add(securityGroup);
                            if (task.IsCanceled)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception exc)
                    {
                        task.SetWarning(string.Format(CultureInfo.CurrentCulture, "An error occurred while processing Team Project \"{0}\"", teamProject.Name), exc);
                    }
                    if (task.IsCanceled)
                    {
                        task.Status = "Canceled";
                        break;
                    }
                }

                e.Result = securityGroups;
            };
            worker.RunWorkerCompleted += (sender, e) =>
            {
                if (e.Error != null)
                {
                    Logger.Log("An unexpected exception occurred while retrieving security groups", e.Error);
                    task.SetError(e.Error);
                    task.SetComplete("An unexpected exception occurred");
                }
                else
                {
                    this.SecurityGroups = (ICollection<SecurityGroupInfo>)e.Result;
                    task.SetComplete("Retrieved " + this.SecurityGroups.Count().ToCountString("security group"));
                }
            };
            worker.RunWorkerAsync();
        }

        #endregion

        #region DeleteSelectedSecurityGroups Command

        private bool CanDeleteSelectedSecurityGroups(object argument)
        {
            return this.SelectedSecurityGroups != null && this.SelectedSecurityGroups.Any();
        }

        private void DeleteSelectedSecurityGroups(object argument)
        {
            var result = MessageBox.Show("This will delete the selected security groups. Are you sure you want to continue?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var securityGroupsToDelete = this.SelectedSecurityGroups.ToList();
                var task = new ApplicationTask("Deleting security groups", securityGroupsToDelete.Count, true);
                PublishStatus(new StatusEventArgs(task));
                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    var tfs = GetSelectedTfsTeamProjectCollection();
                    var securityService = tfs.GetService<IIdentityManagementService>();

                    var step = 0;
                    var count = 0;
                    foreach (var securityGroup in securityGroupsToDelete)
                    {
                        task.SetProgress(step++, string.Format(CultureInfo.CurrentCulture, "Deleting security group \"{0}\" in Team Project \"{1}\"", securityGroup.Name, securityGroup.TeamProject.Name));
                        try
                        {
                            securityService.DeleteApplicationGroup(IdentityHelper.CreateTeamFoundationDescriptor(securityGroup.Sid));
                            count++;
                        }
                        catch (Exception exc)
                        {
                            task.SetWarning(string.Format(CultureInfo.CurrentCulture, "An error occurred while deleting security group \"{0}\" in Team Project \"{1}\"", securityGroup.Name, securityGroup.TeamProject.Name), exc);
                        }
                        if (task.IsCanceled)
                        {
                            task.Status = "Canceled";
                            break;
                        }
                    }

                    e.Result = count;
                };
                worker.RunWorkerCompleted += (sender, e) =>
                {
                    if (e.Error != null)
                    {
                        Logger.Log("An unexpected exception occurred while deleting security groups", e.Error);
                        task.SetError(e.Error);
                        task.SetComplete("An unexpected exception occurred");
                    }
                    else
                    {
                        var count = (int)e.Result;
                        task.SetComplete("Deleted " + count.ToCountString("security group"));
                    }

                    // Refresh the list.
                    GetSecurityGroups(null);
                };
                worker.RunWorkerAsync();
            }
        }

        #endregion

        #region ExportSelectedSecurityGroupPermissions Command

        private bool CanExportSelectedSecurityGroupPermissions(object argument)
        {
            return this.SelectedSecurityGroups != null && this.SelectedSecurityGroups.Any();
        }

        private void ExportSelectedSecurityGroupPermissions(object argument)
        {
            var exportRequests = new List<SecurityGroupPermissionExportRequest>();
            var securityGroups = this.SelectedSecurityGroups;
            if (securityGroups.Count == 1)
            {
                // Export to a single file.
                var securityGroup = securityGroups.Single();
                var dialog = new SaveFileDialog();
                dialog.FileName = securityGroup.Name + ".xml";
                dialog.Filter = "XML Files (*.xml)|*.xml";
                var result = dialog.ShowDialog(Application.Current.MainWindow);
                if (result == true)
                {
                    exportRequests.Add(new SecurityGroupPermissionExportRequest(securityGroup, dialog.FileName));
                }
            }
            else
            {
                // Export to a directory structure.
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "Please select the path where to export the permission files (*.xml). They will be stored in a folder per Team Project.";
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    var rootFolder = dialog.SelectedPath;
                    foreach (var securityGroup in securityGroups)
                    {
                        var fileName = Path.Combine(rootFolder, securityGroup.TeamProject.Name, securityGroup.Name + ".xml");
                        exportRequests.Add(new SecurityGroupPermissionExportRequest(securityGroup, fileName));
                    }
                }
            }

            if (exportRequests.Any())
            {
                var task = new ApplicationTask("Exporting " + exportRequests.Count.ToCountString("security group permission"), exportRequests.Count, true);
                PublishStatus(new StatusEventArgs(task));
                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    SecurityManager.ExportPermissions(this.Logger, task, this.GetSelectedTfsTeamProjectCollection(), this.SelectedTeamProjectCollection.TeamFoundationServer.MajorVersion, exportRequests);
                };
                worker.RunWorkerCompleted += (sender, e) =>
                {
                    if (e.Error != null)
                    {
                        Logger.Log("An unexpected exception occurred while exporting security group permission", e.Error);
                        task.SetError(e.Error);
                        task.SetComplete("An unexpected exception occurred");
                    }
                    else
                    {
                        task.SetComplete("Exported " + exportRequests.Count.ToCountString("security group permission"));
                    }
                };
                worker.RunWorkerAsync();
            }
        }

        #endregion

        #region ResetSecurityPermissions Command

        private bool CanResetSecurityPermissions(object argument)
        {
            return true;
        }

        private void ResetSecurityPermissions(object argument)
        {
            this.SecurityGroupChange.ResetPermissionChanges();
        }

        #endregion

        #region LoadSecurityPermissions Command

        private bool CanLoadSecurityPermissions(object argument)
        {
            return this.SecurityGroupChange.PermissionGroupChanges.Any();
        }

        private void LoadSecurityPermissions(object argument)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Please select the security permissions file (*.xml) to load.";
            dialog.Filter = "XML Files (*.xml)|*.xml";
            var result = dialog.ShowDialog(Application.Current.MainWindow);
            if (result == true)
            {
                try
                {
                    var persistedPermissions = PermissionChangePersistenceData.Load(dialog.FileName);
                    this.SecurityGroupChange.ResetPermissionChanges();
                    foreach (var persistedPermission in persistedPermissions)
                    {
                        var group = this.SecurityGroupChange.PermissionGroupChanges.FirstOrDefault(g => g.PermissionGroup.Scope == persistedPermission.Scope);
                        if (group != null)
                        {
                            var permission = group.PermissionChanges.FirstOrDefault(p => p.Permission.Scope == persistedPermission.Scope && string.Equals(p.Permission.PermissionConstant, persistedPermission.Name, StringComparison.OrdinalIgnoreCase));
                            if (permission != null)
                            {
                                permission.Action = persistedPermission.Action;
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    this.Logger.Log(string.Format(CultureInfo.CurrentCulture, "An error occurred while loading the security permissions from \"{0}\"", dialog.FileName), exc);
                    MessageBox.Show("An error occurred while loading the security permissions. See the log file for details", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        #endregion

        #region SaveSecurityPermissions Command

        private bool CanSaveSecurityPermissions(object argument)
        {
            return true;
        }

        private void SaveSecurityPermissions(object argument)
        {
            var dialog = new SaveFileDialog();
            dialog.Title = "Please select the security permissions file (*.xml) to save.";
            dialog.Filter = "XML Files (*.xml)|*.xml";
            var result = dialog.ShowDialog(Application.Current.MainWindow);
            if (result == true)
            {
                try
                {
                    PermissionChangePersistenceData.Save(dialog.FileName, this.SecurityGroupChange.PermissionGroupChanges.SelectMany(g => g.PermissionChanges).Select(p => new PermissionChangePersistenceData(p)).ToArray());
                }
                catch (Exception exc)
                {
                    this.Logger.Log(string.Format(CultureInfo.CurrentCulture, "An error occurred while saving the security permissions to \"{0}\"", dialog.FileName), exc);
                    MessageBox.Show("An error occurred while saving the security permissions. See the log file for details", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        #endregion

        #region AddOrUpdateSecurityGroup Command

        private bool CanAddOrUpdateSecurityGroup(object argument)
        {
            return IsAnyTeamProjectSelected() && !string.IsNullOrEmpty(this.SecurityGroupChange.Name);
        }

        private void AddOrUpdateSecurityGroup(object argument)
        {
            var result = MessageBox.Show("This will add a new security group to all selected Team Projects, or update the existing group if it already exists. Are you sure you want to continue?", "Confirm Add", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var teamProjects = this.SelectedTeamProjects.ToList();
                var task = new ApplicationTask("Adding / updating security groups", teamProjects.Count, true);
                PublishStatus(new StatusEventArgs(task));
                var worker = new BackgroundWorker();
                worker.DoWork += (sender, e) =>
                {
                    var tfs = GetSelectedTfsTeamProjectCollection();
                    var tfsVersion = this.SelectedTeamProjectCollection.TeamFoundationServer.MajorVersion;

                    var step = 0;
                    var count = 0;
                    foreach (var teamProject in teamProjects)
                    {
                        task.SetProgress(step++, string.Format(CultureInfo.CurrentCulture, "Adding / updating security group \"{0}\" for Team Project \"{1}\"", this.SecurityGroupChange.Name, teamProject.Name));
                        try
                        {
                            SecurityManager.Apply(task, tfs, tfsVersion, teamProject.Name, this.SecurityGroupChange);
                            count++;
                        }
                        catch (Exception exc)
                        {
                            task.SetWarning(string.Format(CultureInfo.CurrentCulture, "An error occurred while adding / updating security group \"{0}\" for Team Project \"{1}\"", this.SecurityGroupChange.Name, teamProject.Name), exc);
                        }
                        if (task.IsCanceled)
                        {
                            task.Status = "Canceled";
                            break;
                        }
                    }

                    e.Result = count;
                };
                worker.RunWorkerCompleted += (sender, e) =>
                {
                    if (e.Error != null)
                    {
                        Logger.Log("An unexpected exception occurred while adding / updating security groups", e.Error);
                        task.SetError(e.Error);
                        task.SetComplete("An unexpected exception occurred");
                    }
                    else
                    {
                        var count = (int)e.Result;
                        task.SetComplete("Added / updated " + count.ToCountString("security group"));
                    }
                };
                worker.RunWorkerAsync();
            }
        }

        #endregion
    }
}