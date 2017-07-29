using System;
using System.Data.SqlClient;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.SqlServer.Management.IntegrationServices;

namespace Microsoft.SqlServer.IntegrationServices.Build
{
	public class DeployProjectToCatalogTask : Task
	{
		[Required]
		public ITaskItem[] IsPacFiles { get; set; }
		[Required]
		public string SsisInstance { get; set; }
		[Required]
		public string SsisFolder { get; set; }
		public bool CreateFolder { get; set; }
		public string Catalog { get; set; }
        private Catalog _catalog { get; set; }

		public DeployProjectToCatalogTask()
		{
			Catalog = "SSISDB";
			CreateFolder = true;

        }

		public override bool Execute()
		{
			bool result = true;
			var csb = new SqlConnectionStringBuilder
			          	{
			          		DataSource = SsisInstance, IntegratedSecurity = true, InitialCatalog = Catalog
			          	};

			Log.LogMessage(SR.ConnectingToServer(csb.ConnectionString));
            Management.IntegrationServices.IntegrationServices ssis;
  
			using (var conn = new SqlConnection(csb.ConnectionString))
			{
				try
				{
					conn.Open();
                    ssis = new Management.IntegrationServices.IntegrationServices(conn);
                    _catalog = ssis.Catalogs[Catalog];
				}
				catch (Exception e)
				{
					Log.LogError(SR.ConnectionError);
					Log.LogErrorFromException(e);
					return false;
				}

				foreach (var taskItem in IsPacFiles)
				{
					try
					{
						Log.LogMessage("------");

                        FileInfo isPac = new FileInfo(taskItem.ItemSpec);
                        var catalogFolder = CreateSsisFolder(CreateFolder);
                        var project = DeployProject(catalogFolder, isPac.FullName);

                        string parameterConfigs = Path.Combine(isPac.Directory.FullName, $@"{project.Name}.config");
                        if (File.Exists(parameterConfigs))
                        {
                            ConfigureEnvironment(project, catalogFolder, isPac.Directory.FullName);
                        }
                        
					}
					catch (Exception e)
					{
						Log.LogErrorFromException(e, true);
						result = false;
					}
				}
			}

			return result;
		}

        private void ConfigureEnvironment(ProjectInfo project, CatalogFolder folder, string workingDir)
        {
            string environment = project.Name;
            var newEnv = folder.Environments[environment];
            if (newEnv != null)
            {
                newEnv.Drop();
            }
            newEnv = new EnvironmentInfo(folder, environment, "");
            newEnv.Create();
            if (project.References[environment, folder.Name] != null)
            {
                project.References.Remove(environment, folder.Name);
            }
            project.References.Add(environment, folder.Name);
            AddParametersToEnvironment(workingDir, project.Name, newEnv, project);
        }

        private void AddParametersToEnvironment(string workingDir, string projectName, EnvironmentInfo env, ProjectInfo project)
        {
            string parameterConfigs = Path.Combine(workingDir, $@"{projectName}.config");
            if (File.Exists(parameterConfigs))
            {
                var projectParameters = LoadProjectParameters(parameterConfigs);
                foreach (var p in projectParameters.Parameters)
                {
                    Log.LogMessage(SR.AddProjectParameter(p.Name));
                    TypeCode type = (TypeCode)Int32.Parse(p.Properties["DataType"]);
                    object value = Convert.ChangeType(p.Properties["Value"], type);
                    var sensitive = Convert.ToBoolean(Convert.ToInt32(p.Properties["Sensitive"]));
                    var description = p.Properties["Description"];
                    env.Variables.Add(p.Name, type, value, sensitive, description );
                    env.Alter();
                    project.Parameters[p.Name].Set(ParameterInfo.ParameterValueType.Referenced, p.Name);
                    project.Alter();
                }

            }
        }

        private ProjectParameters LoadProjectParameters(string file)
        {
            var serializer = new XmlSerializer(typeof(ProjectParameters));
            var fileStream = File.OpenRead(file);
            return (ProjectParameters)serializer.Deserialize(fileStream);
        }

        private ProjectInfo DeployProject(CatalogFolder catalogFolder, string ispacFile)
        {
            byte[] bytes = File.ReadAllBytes(ispacFile);
            string projectName = Path.GetFileNameWithoutExtension(ispacFile);
            var results = catalogFolder.DeployProject(projectName, bytes);
            foreach(OperationMessage message in results.Messages)
            {
                Log.LogMessage(message.Message);
            }

            if( results.Status != Operation.ServerOperationStatus.Success)
            {
                throw new Exception($"Deployment failed for project: '{projectName}");
            }

            return catalogFolder.Projects[projectName];
        }

        private CatalogFolder CreateSsisFolder(bool createFolder)
        {
            CatalogFolder catalogFolder = _catalog.Folders[SsisFolder];
            if(catalogFolder == null)
            {
                if (!CreateFolder)
                {
                    throw new Exception($"The Folder '{SsisFolder}' was not found.  Set CreateFolder to true to create it");
                }
                else
                {
                    catalogFolder = new CatalogFolder(_catalog, SsisFolder, "");
                    catalogFolder.Create();
                }
            }
            return catalogFolder;
        }


	}
}
