.PHONY: resetei
resetei:
	cmd -c "C:\Program Files (x86)\Microsoft Visual Studio 14.0\VSSDK\VisualStudioIntegration\Tools\Bin\CreateExpInstance.exe" -Reset -VSInstance=14.0 -RootSuffix=Exp 

msbuild=msbuild.exe -verbosity:m -nologo

.PHONY: build
build:
	${msbuild} BuildOnSave.sln -t:"BuildOnSave:Rebuild" -p:Configuration=Release
	${msbuild} BuildOnSave.sln -t:"BuildOnSave:Rebuild" -p:Configuration=Debug

.PHONY: package
package: build
	cp BuildOnSave/bin/Release/BuildOnSave.vsix /tmp/BuildOnSave.vsix
	cp BuildOnSave/bin/Debug/BuildOnSave.vsix /tmp/BuildOnSave-Debug.vsix

