namespace VoiceToText.Update;

/// <summary>
/// Writes the self-deleting batch shim that outlives the app during an update: it waits for
/// our PID to exit (so all file/native-DLL locks release), runs the installer silently, then
/// relaunches the app (passing the target version so it can confirm success), and finally
/// deletes the staged setup and itself. Windows-only by nature (cmd + Inno Setup).
/// </summary>
internal static class WindowsRelauncher
{
    public static string WriteShim()
    {
        Directory.CreateDirectory(UpdateService.StagingDir);
        var shimPath = Path.Combine(UpdateService.StagingDir, "relaunch.cmd");
        const string script = """
            @echo off
            setlocal
            set "APPPID=%~1"
            set "SETUP=%~2"
            set "APPEXE=%~3"
            set "LOGFILE=%~4"
            set "TARGETVER=%~5"
            set /a tries=0
            :waitloop
            tasklist /FI "PID eq %APPPID%" 2>nul | find /I "VoiceToText.exe" >nul
            if errorlevel 1 goto runsetup
            set /a tries+=1
            if %tries% geq 30 goto runsetup
            timeout /t 1 /nobreak >nul
            goto waitloop
            :runsetup
            "%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /LOG="%LOGFILE%"
            start "" "%APPEXE%" --postupdate "%TARGETVER%"
            del /q "%SETUP%" >nul 2>&1
            endlocal
            (goto) 2>nul & del "%~f0"
            """;
        File.WriteAllText(shimPath, script);
        return shimPath;
    }
}
