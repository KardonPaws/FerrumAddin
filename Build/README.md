# Debug deployment for Revit 2024

Close Revit and build the Debug configuration in Visual Studio. The post-build event runs Deploy-Debug.ps1, copies the current output including PDB and dependencies plus files from DLL, and registers the assembly from %LocalAppData%\Ferrum\Debug\Revit2024.

The installed release registration, pending update and previous pause setting are saved once in %LocalAppData%\Ferrum\DebugDeployment. Updates are paused during debugging; Debug code does not launch the updater. Release builds do not deploy files. To attach the debugger, start Revit and use Debug > Attach to Process > Revit.exe.

To return to the installed release, close Revit and run from the repository:

```powershell
.\Build\Deploy-Debug.ps1 -Restore
```

Debug assemblies are unsigned and Revit may request permission to load them. Deployed files are copied without deleting unrelated files. Resources must be present in DLL or marked Copy to Output Directory in their project.
