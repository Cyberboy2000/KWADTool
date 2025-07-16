using CommandLine;
using CommandLine.Text;

namespace KWADTool
{
	public enum ExportType
	{
		Anims,
		Textures,
		Blobs,
		All
	}

	public class CommandLineOptions
	{
		[Option('e', "extract", HelpText = "Extract resources. Valid types are Anims|Textures|Blobs|All (case insensitive).", MetaValue = "<type>",
			DefaultValue = ExportType.All)]
		public ExportType ExportType { get; set; }

		[Option('i', "input", Required = true, HelpText = "Input KWAD file.", MetaValue = "<file>")]
		public string Input { get; set; }

		[Option('o', "output", HelpText = "(Default: <kwadFileName>.d) Output directory.", MetaValue = "<dir>")]
		public string Output { get; set; }

		[Option('v', "verbose", Required = false, HelpText = "Enable detailed log output.")]
		public bool Verbose { get; set; }

		[Option('p', "pause", Required = false, HelpText = "Pauses the program after extraction is complete.")]
		public bool Pause { get; set; }

		[HelpOption]
		public string GetUsage()
		{
			return HelpText.AutoBuild(this, helpText => HelpText.DefaultParsingErrorsHandler(this, helpText));
		}
	}
}