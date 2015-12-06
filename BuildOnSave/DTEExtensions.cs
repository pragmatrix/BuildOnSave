using EnvDTE;

namespace BuildOnSave
{
	static class DTEExtensions
	{
		public static bool belongsToAnOpenProject(this Document document)
		{
			return document.ProjectItem.ContainingProject.FullName != "";
		}
	}
}
