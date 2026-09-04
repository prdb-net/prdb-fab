# Review shows five frames and never streams an arriving file

An **Arriving File** in the **Review Queue** is visually checked through a
contact sheet of five evenly spaced frames, generated on demand from the file
that is already on disk. The focused review surface puts that local evidence
beside prdb's artwork and Video facts; it never exposes the file as a playable
or downloadable response.

Full playback was rejected because it would cross `VISION.md`'s boundary into
being a media player and bring seeking, range requests, browser codec support
and transcoding into a decision that needs only quick visual evidence. A single
bounded `ffmpeg` process produces the complete contact sheet, so moving through
the queue costs one local request and never five competing decodes.
