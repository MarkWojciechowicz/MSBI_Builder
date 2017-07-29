using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SqlTest;
using System.IO;
using System.Diagnostics;
using System.Configuration;

namespace Microsoft.SqServer.IntegrationServices.Build.Tests
{
    [TestFixture]
    public class DeployProjectToCatalogTask
    {
        public static void RunDeployment(string args)
        {
            string msBuild = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe";
            DirectoryInfo testDir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            var deploy = new Process {
                StartInfo = new ProcessStartInfo
                {
                    FileName = msBuild,
                    Arguments = args,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.Combine(testDir.FullName, "TestFiles"),
                    UseShellExecute = false
                }
             };

            deploy.Start();
            string output = deploy.StandardOutput.ReadToEnd();
            deploy.WaitForExit();

            if(deploy.ExitCode != 0)
            {
                throw new Exception(output);
            } 

        }

        SqlTestTarget ssisdb;
        string ssisServer;
        
        [SetUp]
        public void Setup()
        {
            ssisdb = new SqlTestTarget("ssisdb");
            ssisdb.ExecuteAdhoc(@"if exists(Select 1 From catalog.projects p 
	                                    join catalog.folders f on p.folder_id = f.folder_id 
	                                    where f.name = 'NewFolder'
	                                    and p.name = 'TestSsisProject')
                                    BEGIN
	                                    exec [catalog].[delete_project] 'NewFolder', 'TestSsisProject';
                                    END

                                    if exists(select * from catalog.environments e
	                                    join catalog.folders f on e.folder_id = f.folder_id
	                                    WHERE f.name = 'NewFolder')
                                    BEGIN
	                                    exec catalog.delete_environment 'NewFolder', 'TestSsisProject';
                                    END

                                    if exists(Select 1 From catalog.folders where name = 'NewFolder')
                                    BEGIN
	                                    exec [catalog].[delete_folder] 'NewFolder';
                                    END

                                    ");
            string exeConfigPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var config = ConfigurationManager.OpenExeConfiguration(exeConfigPath);
            ssisServer = config.AppSettings.Settings["ssisServer"].Value.ToString();
        }

        [Test]
        public void DeployProjectToCatalogTask_Execute_DeploysProject()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);

            //Assert
            var actual = ssisdb.GetActual(@"Select 1 From catalog.projects p 
                                        join catalog.folders f on p.folder_id = f.folder_id
                                        where f.name = 'NewFolder'
                                        and p.name = 'TestSsisProject'");
            Assert.That(actual, Is.EqualTo(1));
        }


        [Test]
        public void DeployProjectToCatalogTask_Execute_FolderCreated()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);

            //Assert
            var actual = ssisdb.GetActual(@"Select 1 From catalog.folders f where f.name = 'NewFolder'");
            Assert.That(actual, Is.EqualTo(1));
        }

        [Test]
        public void DeployProjectToCatalogTask_Execute_EnvironmentCreated()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);

            //Assert
            var actual = ssisdb.GetActual(@"select 1 from catalog.environments e
	                                    join catalog.folders f on e.folder_id = f.folder_id
	                                    WHERE f.name = 'NewFolder'");
            Assert.That(actual, Is.EqualTo(1));
        }

        [Test]
        public void DeployProjectToCatalogTask_Execute_VariableAddedToEnvironment()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);

            //Assert
            var actual = ssisdb.GetActual(@"select count(*) from catalog.environments e
	                                        join catalog.environment_variables ev on e.environment_id = ev.environment_id
	                                        WHERE e.name = 'TestSsisProject'");
            Assert.That(actual, Is.EqualTo(5));
        }

        [Test]
        public void DeployProjectToCatalogTask_Execute_ParametersReferencedInProject()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);

            //Assert
            var actual = ssisdb.GetActual(@"select count(referenced_variable_name) from catalog.object_parameters where object_name = 'TestSsisProject'");
            Assert.That(actual, Is.EqualTo(5));
        }

        [Test]
        public void DeployProjectToCatalogTask_ExecutedTwice_ProjectUpdated()
        {
            //act
            string args = $@"SsisBuild.proj /t:SSISDeploy /p:SSISProj=TestSsisProject,Configuration=Development,ProjectName=TestSsisProject,SSISServer={ssisServer},FolderName=NewFolder";
            RunDeployment(args);
            var firstDeployed = ssisdb.GetActual("select last_deployed_time from catalog.projects p where p.name = 'TestSsisProject'");
            RunDeployment(args);

            //Assert
            var lastDeployed = ssisdb.GetActual(@"select last_deployed_time from catalog.projects p where p.name = 'TestSsisProject'");
            Assert.That(lastDeployed, Is.GreaterThan(firstDeployed));
        }

    }
}


