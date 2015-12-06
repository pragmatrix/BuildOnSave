namespace BuildOnSave
{
	enum BuildType
	{
		Solution,
		StartupProject
	}

	sealed class SolutionOptions
	{
		public bool Enabled;
		public BuildType BuildType;
	}
}
