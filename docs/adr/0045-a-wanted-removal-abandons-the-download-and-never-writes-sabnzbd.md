# A wanted removal abandons the download and never writes SABnzbd

When the Wanted feed removes a Video with an Outstanding automatic Download,
the Download becomes `Abandoned` and this tool stops following it. The Release
remains consumed and the Retry Budget remains charged, but no retry follows
because the Video is no longer wanted. The SABnzbd job is never paused or
deleted: its queue belongs to the user, and `addfile` remains the only write
this tool makes to it.

This chooses ADR 0016's ownership boundary over ADR 0007's earlier delete
requirement. Continuing to follow would file content after the user's intent
had been withdrawn; deleting would make a feed event destructive in another
application. Stop following is the existing visible middle action and says
truthfully that SABnzbd may still finish the job on its own.
