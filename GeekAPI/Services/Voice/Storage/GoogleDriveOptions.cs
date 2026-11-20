namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// The Google Drive side of bring-your-own-storage (plans/client-and-app-plan.md, "Google Drive (All Tiers)"). It uses the same OAuth
/// client as the calendar connection, so there is no new variable to set; what differs is the scope and what it is for.
/// </summary>
public static class GoogleDriveOptions
{
    /// <summary>The provider a tenant's Drive connection is stored under.</summary>
    public const string Provider = "google_drive";

    /// <summary>
    /// The one scope the plan allows: per-file access, limited to files this application itself created. It cannot see, open or touch
    /// anything else in the owner's Drive, which is what makes the connection safe to offer on every tier.
    /// </summary>
    public const string FileScope = "https://www.googleapis.com/auth/drive.file";

    /// <summary>The single folder the plan names, which this application creates and is the only thing it can reach.</summary>
    public const string FolderName = "Geek-Receptionist Logs";

    public const string FolderMimeType = "application/vnd.google-apps.folder";

    public const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";
    public const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
}
