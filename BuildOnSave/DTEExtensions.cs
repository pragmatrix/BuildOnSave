using System.Collections.Generic;
using System.Linq;
using EnvDTE;

namespace BuildOnSave
{
	static class DTEExtensions
	{
		public static IEnumerable<Document> unsavedDocumentsBelongingToAProject(this DTE dte)
		{
			return dte.Documents
				.Cast<Document>()
				.Where(document => !document.Saved && document.belongsToAnOpenProject());
		}

		public static IEnumerable<Project> unsavedOpenProjects(this DTE dte)
		{
			return dte.Solution.Projects
				.Cast<Project>()
				.Where(p => !p.Saved);
		}
		public static bool belongsToAnOpenProject(this Document document)
		{
			return document.ProjectItem.ContainingProject.FullName != "";
		}
	}
}
