Setup Operations

1.0 Update References
	1.1 [Absolute Path to Revit Installation Folder]\RevitAPI.dll 
		eg. C:\Program Files\Autodesk\Revit 2022\RevitAPI.dll
	1.2 [Absolute Path to Revit Installation Folder]\RevitAPIUI.dll 
		eg. C:\Program Files\Autodesk\Revit 2022\RevitAPIUI.dll

2.0 Update RevitToJson.addin Manifest
	2.1 [Absolute Path to Assembly]\RevitJson\Binary\RevitToJson.dll
		eg. C:\Users\Username\Source\RevitJson\Binary\RevitToJson.dll

3.0 Update Post Build Events
	3.1 copy "$(ProjectDir)$(TargetName).addin" "[Absolute Path to Revit Addins Folder]"
		eg. copy "$(ProjectDir)$(TargetName).addin" "C:\ProgramData\Autodesk\Revit\Addins\2022\"