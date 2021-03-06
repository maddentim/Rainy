using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;

using log4net;

using Rainy.OAuth;
using JsonConfig;
using Mono.Options;
using Rainy.Db;
using System.Diagnostics;
using log4net.Appender;
using Rainy.Interfaces;

namespace Rainy
{
	public class MainClass
	{
		// HACK a dictionary holding usernames and their repos
		// can be used for locking
		public static Dictionary<string, Semaphore> UserLocks;

		// some Status/Diagnostics
		public static DateTime Uptime;
		public static long ServedRequests;


		protected static void SetupLogging (int loglevel)
		{
			// console appender
			log4net.Appender.ConsoleAppender appender;
			appender = new log4net.Appender.ConsoleAppender ();
			appender.Layout = new log4net.Layout.PatternLayout
				("%-4utcdate{yy/MM/dd_HH:mm:ss.fff} [%-5level] %logger->%M - %message%newline");

			switch (loglevel) {
			case 0: appender.Threshold = log4net.Core.Level.Error; break;
			case 1: appender.Threshold = log4net.Core.Level.Warn; break;
			case 2: appender.Threshold = log4net.Core.Level.Info; break;
			case 3: appender.Threshold = log4net.Core.Level.Debug; break;
			case 4: appender.Threshold = log4net.Core.Level.All; break;
			}

			log4net.Config.BasicConfigurator.Configure (appender);
			LogManager.GetLogger("Logsystem").Debug ("logsystem initialized");

			if (loglevel >= 3) {
				var appender2 = new log4net.Appender.FileAppender (appender.Layout, "./debug.log", true);
				log4net.Config.BasicConfigurator.Configure (appender2);
				LogManager.GetLogger("Logsystem").Debug ("Writing all log messages to file: debug.log");
			}

			/* ColoredConsoleAppender is win32 only. A managed version was introduced to log4net svn
			and should be available when log4net 1.2.12 comes out.
		
			Below codes is not tested/working!	
				
			log4net.Appender.ColoredConsoleAppender appender;
			appender = new log4net.Appender.ColoredConsoleAppender ();
			appender.Layout = new log4net.Layout.PatternLayout ("%date [%thread] %-5level %logger [%property{NDC}] - %message%newline");
			log4net.Config.XmlConfigurator.Configure(new System.IO.FileInfo("/Users/td/log4net.config"));
			colors.BackColor = log4net.Appender.ColoredConsoleAppender.Colors.HighIntensity;
			colors.ForeColor = log4net.Appender.ColoredConsoleAppender.Colors.Blue;
			colors.Level = log4net.Core.Level.Debug;
			appender.AddMapping(colors);	
			*/	
		}
		public static void Main (string[] args)
		{
			// parse command line arguments
			string config_file = "settings.conf";
			int loglevel = 0;
			bool show_help = false;
			bool open_browser = true;

			var p = new OptionSet () {
				{ "c|config=", "use config file",
					(string file) => config_file = file },
				{ "v", "increase log level, where -vvvv is highest",
					v => { if (v != null) ++loglevel; } },
				{ "h|help",  "show this message and exit", 
					v => show_help = v != null },
				{ "b|nobrowser",  "do not open browser window upon start",
					v => { if (v != null) open_browser = false; } },
			};
			p.Parse (args);

			if (show_help) {
				p.WriteOptionDescriptions (Console.Out);
				return;
			}

			if (!File.Exists (config_file)) {
				Console.WriteLine ("Could not find a configuration file (try the -c flag)!");
				return;
			}

			// set the configuration from the specified file
			Config.Global = Config.ApplyJsonFromPath (config_file);

			string data_path = Config.Global.DataPath;
			if (string.IsNullOrEmpty (data_path)) {
				data_path = Directory.GetCurrentDirectory ();
			}
			var sqlite_file = Path.Combine (data_path, "rainy.db");
			DbConfig.SetSqliteFile (sqlite_file);

			SetupLogging (loglevel);

			string listen_hostname = Config.Global.ListenAddress;
			int listen_port = Config.Global.ListenPort;

			// determine and setup data backend
			string backend = Config.Global.Backend;

			IDataBackend data_backend;
			// by default we use the filesystem backend
			if (string.IsNullOrEmpty (backend)) {
				backend = "filesystem";
			}

			if (backend == "sqlite") {

				if (string.IsNullOrEmpty (Config.Global.AdminPassword)) {
					Console.WriteLine ("FATAL: Field 'AdminPassword' in the settings config may not " +
					                   "be empty when using the sqlite backend");
					return;
				}
				data_backend = new DatabaseBackend (data_path, reset: false);
			} else {

				// simply use user/password list from config for authentication
				CredentialsVerifier config_authenticator = (username, password) => {
					// call the authenticater callback
					if (string.IsNullOrEmpty (username) || string.IsNullOrEmpty (password))
					return false;

					foreach (dynamic credentials in Config.Global.Users) {
						if (credentials.Username == username && credentials.Password == password)
							return true;
					}
					return false;
				};

				data_backend = new RainyFileSystemBackend (data_path, config_authenticator);
			}

			string listen_url = "http://" + listen_hostname + ":" + listen_port + "/";

			string admin_ui_url = listen_url.Replace ("*", "localhost");

			if (open_browser) {
				admin_ui_url += "admin/#?admin_pw=" + Config.Global.AdminPassword;
			}

			using (var listener = new RainyStandaloneServer (data_backend, listen_url)) {

				listener.Start ();
				Uptime = DateTime.UtcNow;

				if (open_browser) {
					Process.Start (admin_ui_url);
				}

				Console.WriteLine ("Press RETURN to stop Rainy");
				Console.ReadLine ();
			}
		}
	}
}