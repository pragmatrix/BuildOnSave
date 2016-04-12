using System;

namespace BuildOnSave
{
	enum BuildType
	{
		Solution,
		StartupProject,
		ProjectsOfSavedFiles
	}

	sealed class SolutionOptions
	{
		public bool Enabled;
		public BuildType BuildType;
	}
}
