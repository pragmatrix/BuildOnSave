using System.Collections.Generic;
using System.Linq;
using EnvDTE;

namespace BuildOnSave
{
	static class DTEExtensions
	{
		public static Document[] unsavedDocumentsBelongingToAProject(this DTE dte)
		{
			// note: this might have the side effect of opening a project's property page
			// and throwing a COMException.
			var allDocuments = dte.Documents.Cast<Document>().ToArray();
			var unsavedDocuments = allDocuments.Where(document => !document.Saved).ToArray();
			var unsavedBelongingToAnOpenProject = unsavedDocuments
				.Where(document => document.belongsToAnOpenProject())
				.ToArray();

			return unsavedBelongingToAnOpenProject;
		}

		public static Project[] unsavedOpenProjects(this DTE dte)
		{
			return dte.Solution.Projects
				.Cast<Project>()
				.Where(p => !p.Saved)
				.ToArray();
		}
		public static bool belongsToAnOpenProject(this Document document)
		{
			return document.ProjectItem.ContainingProject.FullName != "";
		}
	}
}
