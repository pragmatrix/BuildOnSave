namespace BuildOnSave
{
	enum BuildType
	{
		Solution,
		StartUpProject
	}

	sealed class SolutionOptions
	{
		public bool Enabled;
		public BuildType BuildType;
	}
}
