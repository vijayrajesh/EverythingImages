; Inno Setup script: the PORTABLE installer, and the only one EverythingImages has.
;
; It asks where to put EverythingImages and copies it there - nothing else. No
; uninstaller, no entry in Apps & features, no Start menu, no registry. The
; portable.txt it lays down beside the exe is what makes the app keep its index,
; its settings and its AI models in a "data" folder inside that same folder
; (see Paths.cs), so the folder is the whole app: copy it to a USB stick and it
; comes too, delete it and it is gone.
;
; The one thing that is not in the folder is the .NET 10 Desktop Runtime, which
; the exe needs and which belongs to Windows. This setup itself needs nothing:
; it is a native program and runs on a bare PC. Only when the runtime is
; missing does it ask - a plain Yes/No before copying - and No still copies the
; app, which then shows .NET's own "Download it now" box when it is started.
;
; Not run by hand - build-installer.bat publishes ..\dist first, reads the
; version from src\EverythingImages\EverythingImages.csproj and passes it in as AppVersion.
; Compiling this on its own stops at the #error below rather than packaging a
; stale dist.

#ifndef AppVersion
  #error Run build-installer.bat instead of compiling this by hand
#endif

#define AppName    "EverythingImages"
#define AppExe     "EverythingImages.exe"
#define Publisher  "Rajeshkannan MJ"
#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
; Its own AppId, so nothing here may take over, or be mistaken for, a real
; installation of another product.
AppId={{15FCABFD-9D89-4DFF-9416-F015C3F63554}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion} (portable)
AppPublisher={#Publisher}
VersionInfoVersion={#AppVersion}

; portable: nothing registered, nothing to uninstall
Uninstallable=no
CreateUninstallRegKey=no
UsePreviousAppDir=no
PrivilegesRequired=lowest

; The folder is the one real question, so the page is always shown, and its
; Browse button opens the Windows folder picker (see [Code]). The default sits
; in the user's own profile: writable without admin, and outside Documents and
; Desktop, which OneDrive often syncs - AI models included.
DefaultDirName={%USERPROFILE}\{#AppName}
DisableDirPage=no
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableReadyPage=no
; a portable folder in the profile is the point, not a mistake to warn about
UsedUserAreasWarning=no

OutputDir=..\release
OutputBaseFilename={#AppName}-Portable-Setup-{#AppVersion}
SetupIconFile=..\src\EverythingImages\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

; Putting a newer version into the same folder usually finds the old one running.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This puts a portable copy of [name/ver] into a folder of your choice.%n%nNothing is installed into Windows: the program, its settings, its index and the AI models it downloads all stay in that folder. Pick a USB stick to carry it with you, and delete the folder to remove it.
SelectDirDesc=Which folder should the portable copy go in?
SelectDirLabel3=Everything [name] needs, and everything it saves, stays in this folder - the AI models included, so leave room for them. Browse to Desktop, Documents, a USB stick: any folder you can write to.
FinishedHeadingLabel=[name] is ready
; The finished text names the folder, and {app} is not expanded in [Messages]
; (it showed as "{app}"), so CurPageChanged writes it.

[Files]
Source: "..\dist\{#AppExe}";    DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\ai-engine\*";  DestDir: "{app}\ai-engine"; Flags: ignoreversion recursesubdirs createallsubdirs
; What makes the copy portable (Paths.IsPortable). A portable copy is the only
; thing this setup makes, so it always lays the marker down - and refreshes its
; text, which explains the folder. Deleting it afterwards is how someone moves
; that copy back to the per-user folders.
Source: "portable.txt";         DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  InnoBrowse: TNotifyEvent;

{ ---------------------------------------------------------------------------
  The .NET 10 Desktop Runtime. The exe is framework-dependent, so it runs on
  the shared runtime rather than carrying one; that keeps the portable folder
  small, at the price of this one thing living outside it.
  --------------------------------------------------------------------------- }

{ True when no 10.x WindowsDesktop runtime is on the PC. Looked for in both
  places .NET records it: the folder of the usual install, and the registry
  key its installer writes, which also covers a runtime put somewhere else. }
function NeedsDotNet: Boolean;
var
  Found: TFindRec;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := True;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Found) then
  begin
    Result := False;
    FindClose(Found);
    exit;
  end;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Result := False;
        exit;
      end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

function ContinueWithoutRuntime(const Why: String): Boolean;
begin
  Result := SuppressibleMsgBox(Why + #13#10#13#10 +
    'Copy EverythingImages anyway? When you start it, it will offer to download the .NET 10 ' +
    'Desktop Runtime (or get it from https://dotnet.microsoft.com/download/dotnet/10.0).',
    mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

function InstallRuntime: Boolean;
var
  Code: Integer;
begin
  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if DownloadPage.AbortedByUser then
        Result := ContinueWithoutRuntime('The .NET download was cancelled.')
      else
        Result := ContinueWithoutRuntime('The .NET download failed: ' + GetExceptionMessage);
      exit;
    end;
    DownloadPage.SetText('Installing the .NET 10 Desktop Runtime...', 'Windows may ask for permission.');
    // The runtime installs for the whole PC, so it asks for elevation itself.
    if not ShellExec('', ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'),
                     '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
      Result := ContinueWithoutRuntime('The .NET installer could not start: ' + SysErrorMessage(Code))
    else if (Code <> 0) and (Code <> 3010) then
      Result := ContinueWithoutRuntime('The .NET installer ended with code ' + IntToStr(Code) + '.')
    else
      Result := True;
  finally
    DownloadPage.Hide;
  end;
end;

{ Asked once, as the last thing before copying, and only when the runtime is
  missing. A silent run answers No: nothing is downloaded, and nothing asks
  Windows for permission, without someone there to agree to it. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or not NeedsDotNet then
    exit;
  Log('The .NET 10 Desktop Runtime is missing; asking whether to install it.');
  if SuppressibleMsgBox('EverythingImages needs the .NET 10 Desktop Runtime, and this PC does not have it yet.' + #13#10#13#10 +
      'Download and install it now? It is about 60 MB, comes from Microsoft, and Windows will ask for permission.' + #13#10#13#10 +
      'Choose No to copy EverythingImages without it. It will then offer the download when you start it.',
      mbConfirmation, MB_YESNO, IDNO) = IDYES then
    Result := InstallRuntime
  else
    Log('Copying without the runtime.');
end;

{ The Ready page lists the runtime too, so the question that follows it is
  no surprise. }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if NeedsDotNet then
    Result := Result + NewLine + NewLine + 'Also needed:' + NewLine + Space +
      '.NET 10 Desktop Runtime - not on this PC yet. You will be asked whether to install it.';
end;

{ The finished page names the folder the copy went to, and - when it finished
  without the runtime (No, or the install failed) - says what happens next,
  rather than let "Start EverythingImages now" be the first anyone hears of it. }
procedure CurPageChanged(CurPageID: Integer);
var
  Text: String;
begin
  if CurPageID <> wpFinished then
    exit;
  Text := 'The portable copy is in:' + #13#10 + ExpandConstant('{app}') + #13#10#13#10 +
    'There is no Start menu entry. To remove it, delete the folder.';
  if NeedsDotNet then
    Text := Text + #13#10#13#10 +
      'It still needs the .NET 10 Desktop Runtime, and will offer to download it when you start it.';
  WizardForm.FinishedLabel.Caption := Text + #13#10#13#10 + SetupMessage(msgClickFinish);
  { AdjustHeight measured this text short and clipped its last lines, so the
    label gets room for all of it, and "Start EverythingImages now" goes below that. }
  WizardForm.FinishedLabel.Height := ScaleY(185);
  WizardForm.RunList.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(8);
end;

{ ---------------------------------------------------------------------------
  Browse... opens the Windows folder picker - the one Explorer uses, with
  Desktop, Documents, Downloads, This PC and any USB stick in its left pane -
  instead of Inno's own folder tree, where even the Desktop is awkward to
  reach. For a portable app the folder is the one question that matters.

  The picker is IFileOpenDialog with FOS_PICKFOLDERS, reached through COM.
  Only the methods used are declared properly; the rest are placeholders that
  keep every later method at its right place in the table. If anything about
  it fails, Inno's own Browse runs instead, so the button always works.
  --------------------------------------------------------------------------- }

const
  CLSID_FileOpenDialog = '{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}';
  IID_IShellItem = '{43826D1E-E718-42EE-BC55-A1E261C37BFE}';
  FOS_PICKFOLDERS = $20;
  FOS_FORCEFILESYSTEM = $40;
  FOS_PATHMUSTEXIST = $800;
  SIGDN_FILESYSPATH = $80058000;

type
  IShellItem = interface(IUnknown)
    '{43826D1E-E718-42EE-BC55-A1E261C37BFE}'
    procedure BindToHandler;
    procedure GetParent;
    { the name comes back as a raw pointer: Inno's own String marshalling reads
      it wrongly and crashes, so it is copied out by hand (see PathFromPointer) }
    function GetDisplayName(sigdnName: Cardinal; out ppszName: Cardinal): HResult;
    procedure GetAttributes;
    procedure Compare;
  end;

  IFileDialog = interface(IUnknown)
    '{42F85136-DB7E-439C-85F1-E4075D135FC8}'
    function Show(hwndOwner: HWND): HResult;
    procedure SetFileTypes;
    procedure SetFileTypeIndex;
    procedure GetFileTypeIndex;
    procedure Advise;
    procedure Unadvise;
    function SetOptions(fos: Cardinal): HResult;
    function GetOptions(out pfos: Cardinal): HResult;
    procedure SetDefaultFolder;
    function SetFolder(psi: IShellItem): HResult;
    procedure GetFolder;
    procedure GetCurrentSelection;
    procedure SetFileName;
    procedure GetFileName;
    function SetTitle(pszTitle: String): HResult;
    function SetOkButtonLabel(pszText: String): HResult;
    procedure SetFileNameLabel;
    function GetResult(out ppsi: IShellItem): HResult;
  end;

function SHCreateItemFromParsingName(pszPath: String; pbc: Cardinal; const riid: TGUID; out ppv: IShellItem): HResult;
  external 'SHCreateItemFromParsingName@shell32.dll stdcall';
function lstrlenW(p: Cardinal): Integer;
  external 'lstrlenW@kernel32.dll stdcall';
procedure RtlMoveMemory(Dest: String; Src: Cardinal; Bytes: Cardinal);
  external 'RtlMoveMemory@kernel32.dll stdcall';
procedure CoTaskMemFree(p: Cardinal);
  external 'CoTaskMemFree@ole32.dll stdcall';

{ A string Windows allocated, copied into a Pascal string and then freed. }
function PathFromPointer(p: Cardinal): String;
var
  Len: Integer;
begin
  Result := '';
  if p = 0 then
    Exit;
  Len := lstrlenW(p);
  SetLength(Result, Len);
  if Len > 0 then
    RtlMoveMemory(Result, p, Len * 2);
  CoTaskMemFree(p);
end;

{ The folder the picker should open in: the nearest folder of the current
  path that exists, so it starts where the box already points. }
function ExistingStart(Path: String): String;
begin
  Result := RemoveBackslashUnlessRoot(Path);
  while (Result <> '') and not DirExists(Result) do
  begin
    if ExtractFileDir(Result) = Result then
    begin
      Result := '';
      Exit;
    end;
    Result := ExtractFileDir(Result);
  end;
end;

procedure ModernBrowse(Sender: TObject);
var
  Dialog: IFileDialog;
  Start, Picked: IShellItem;
  Options, NamePointer: Cardinal;
  StartDir, Path: String;
begin
  try
    Dialog := IFileDialog(CreateComObject(StringToGuid(CLSID_FileOpenDialog)));
    OleCheck(Dialog.GetOptions(Options));
    OleCheck(Dialog.SetOptions(Options or FOS_PICKFOLDERS or FOS_FORCEFILESYSTEM or FOS_PATHMUSTEXIST));
    OleCheck(Dialog.SetTitle('Where should the portable copy go?'));
    OleCheck(Dialog.SetOkButtonLabel('Use this folder'));

    StartDir := ExistingStart(WizardForm.DirEdit.Text);
    if (StartDir <> '') and
       (SHCreateItemFromParsingName(StartDir, 0, StringToGuid(IID_IShellItem), Start) = 0) then
      Dialog.SetFolder(Start);

    { anything but S_OK is Cancel or the dialog closing: leave the box alone }
    if Dialog.Show(WizardForm.Handle) <> 0 then
      Exit;

    OleCheck(Dialog.GetResult(Picked));
    OleCheck(Picked.GetDisplayName(SIGDN_FILESYSPATH, NamePointer));
    Path := PathFromPointer(NamePointer);
    if Path = '' then
      Exit;

    { Like Inno's own Browse: the app gets a folder of its own inside the one
      picked, unless the one picked already is it. }
    Path := RemoveBackslashUnlessRoot(Path);
    if CompareText(ExtractFileName(Path), '{#AppName}') <> 0 then
      Path := AddBackslash(Path) + '{#AppName}';
    WizardForm.DirEdit.Text := Path;
  except
    InnoBrowse(Sender);
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
  InnoBrowse := WizardForm.DirBrowseButton.OnClick;
  WizardForm.DirBrowseButton.OnClick := @ModernBrowse;
end;
