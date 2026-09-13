# Can a media server be given a timeline preview we did not generate?

An investigation, run on 2026-09-13, into whether the sprite sheet and paired
WebVTT prdb publishes as a User Preview can be written beside a Library Video
File and used by a media server for timeline scrubbing.

**The answer is no, for both targets, and one of the two ways of trying it is
actively harmful.** A supported path into Jellyfin exists, but it is a different
thing from publishing what prdb gave us, and taking it is a decision with a
cost that a person has to weigh. Nothing has been built, ADR 0027 is unamended,
and the choice is left open at the end of this document rather than made
quietly.

## What was tested

| | |
| --- | --- |
| Jellyfin | **10.11.11**, the official `jellyfin/jellyfin` image, in Docker |
| Client | The server's own HLS image playlist, which is what every client reads |
| Video | 120 s, 640x360, one large second counter burned into every frame, so a scrub position can be read straight off a tile |
| Preview | 12 tiles at 10 s, 4 columns x 3 rows of 320x180 — one sheet of 1280x540 — with a WebVTT naming each tile's seconds and `#xywh` rectangle |
| Plex | Documentation and source behaviour only; see below for why no run was needed |

The video and the sheet were both produced with `ffmpeg` from the same source,
so the tiles genuinely are the frames at those seconds. Everything below is a
verbatim reading from the running server.

## Jellyfin: how it actually works

Read out of the source at `v10.11.11` rather than inferred, and then confirmed
against the running server.

`PathManager.GetTrickplayDirectory` puts the assets beside the media, when the
library option says so, at

```
<video basename>.trickplay/<width> - <tileWidth>x<tileHeight>/<n>.jpg
```

— so with the default settings, `Timecode Scene (2026).trickplay/320 - 10x10/0.jpg`.

`TrickplayOptions` supplies every number in that path and the timing with it:
`WidthResolutions` (default `[320]`), `TileWidth` and `TileHeight` (default
`10`), and `Interval` (default `10000` ms). **They are server-wide settings.**

`TrickplayManager.RefreshTrickplayDataAsync` contains, in as many words, an
*import existing trickplay tiles* branch: where the directory exists, is not
being replaced, and holds files, it does not regenerate. It reads the tiles and
writes a `TrickplayInfo` from them:

```csharp
ThumbnailCount = existingFiles.Length,
…
localTrickplayInfo.Height = Math.Max(
    localTrickplayInfo.Height,
    (int)Math.Ceiling((double)image.Height / localTrickplayInfo.TileHeight));
```

Three facts follow, and they are the whole finding:

1. **Nothing on disk carries the timing.** There is no manifest file. `Interval`
   comes from the server's settings, and the WebVTT is never opened.
2. **The grid is assumed, not measured.** `TileWidth` and `TileHeight` come from
   the settings too; the only thing read off the image is its pixel height,
   divided by the assumed rows.
3. **`ThumbnailCount` counts *files*, not thumbnails.** One sheet is one
   thumbnail as far as the import is concerned.

## What the server did with our pair

The library was configured with `SaveTrickplayWithMedia`,
`EnableTrickplayImageExtraction` and `ExtractTrickplayImagesDuringLibraryScan`,
and the trickplay settings left at their defaults — the ones our directory was
built for. The scan found the item and imported the sheet.

**Imported**, as the server then described it:

```json
{"Width": 320, "Height": 54, "TileWidth": 10, "TileHeight": 10,
 "ThumbnailCount": 1, "Interval": 10000, "Bandwidth": 120}
```

**Generated** by the same server, from the same video, with the trickplay
directory removed — the control:

```json
{"Width": 320, "Height": 180, "TileWidth": 10, "TileHeight": 10,
 "ThumbnailCount": 12, "Interval": 10000, "Bandwidth": 407}
```

And the playlist every client actually reads, side by side:

```
### generated                             ### imported
#EXTINF:120,                              #EXTINF:10,
#EXT-X-TILES:RESOLUTION=320x180,          #EXT-X-TILES:RESOLUTION=320x54,
             LAYOUT=10x10,DURATION=10                  LAYOUT=10x10,DURATION=10
0.jpg  (3200x1800)                        0.jpg  (1280x540)
```

The import is served, and it is wrong in every dimension that matters. A client
following the second playlist crops **320x54** cells out of a **10x10** grid
over an image that is four columns of 180-pixel tiles: every thumbnail it draws
is a horizontal third of the wrong picture, and the playlist claims the strip
covers **ten seconds** of a **two-minute** film. The scrub does not merely look
worse — it points somewhere else.

**Playback of the file itself was unaffected.** The video plays; it is the
scrubbing strip that is wrong.

## The harmful half

The other way of reading *sidecar* — the pair beside the video, named after it,
the way `movie.nfo` and `fanart.jpg` are — was tested at the same time. The
sheet was ignored, as expected. The WebVTT was not:

```
stream: Subtitle  webvtt  /media/…/Timecode Scene (2026).vtt  external: True
```

**A WebVTT beside a Video File is an external subtitle track to Jellyfin.**
Writing one would put a bogus entry in the subtitle menu of every client, on
every file this tool filed. That is not an unsupported feature quietly doing
nothing; it is a visible defect in something that works today, and it rules the
naive layout out on its own.

## Plex

Not run, and it did not need to be. Plex's own documentation describes video
preview thumbnails as **BIF** index files named `index-sd.bif`, generated by the
server and stored inside its own data directory alongside the rest of its media
database. There is a standing community request to have Plex *read* BIF files
from media folders as local assets, which is the clearest possible evidence that
it does not.

Writing into Plex's data directory is the one thing this investigation was told
not to do, and it would be wrong anyway: it is a private store with its own
identifiers, its own lifecycle and no contract. **Plex is unsupported, and there
is nothing to propose short of an upstream feature this project does not
control.**

## What a supported Jellyfin path would actually cost

There is one, and it is worth being precise about what it is not. It is **not**
publishing what prdb gave us. It is **re-cutting** prdb's sheet into the
server's own grid at the server's own interval, and writing the result in the
layout above. Three things stand in the way, and none of them is a detail:

- **The interval is the server's, and it is uniform.** prdb's previews carry
  their own times — a hundred tiles across a twenty-two minute scene is 13.2
  seconds a tile — and Jellyfin has one number for every file in the library.
  Fitting one to the other means resampling: for each ten-second slot, the
  nearest tile prdb happens to have. Some tiles are shown twice and some are
  never shown. It is a visible compromise rather than a lossless conversion.
- **The three settings cannot be read.** `Interval`, `WidthResolutions`,
  `TileWidth` and `TileHeight` live in the server's configuration.
  `VISION.md` has this tool write files into a library, not talk to a media
  server's API, and guessing the defaults means the assets are silently wrong
  for anybody who has changed them.
- **It is a second image pipeline.** Re-cutting means decoding prdb's sheet,
  reordering its tiles and re-encoding a new one. `ffmpeg` is already a
  dependency (ADR 0053), so this is possible rather than impossible — but it is
  work per Video File, on the machine, and it is not what "copy the pair beside
  the file" implied.

## Where this leaves the work

- **ADR 0027 is unamended.** It is amended only for a demonstrated path, and
  what was demonstrated is that the intended one does not exist. `movie.nfo` and
  `fanart.jpg` are untouched.
- **Nothing is written beside a Video File**, and the question below is left
  open rather than answered on somebody's behalf. Reducing the Library's scope
  and authorising a re-cut are both real answers, and both belong to a person.
- **Nothing was sent anywhere.** The Jellyfin behaviour is worth reporting
  upstream — `ThumbnailCount` counting files is a defect on its own terms — but
  reporting it is a separate act.

## The open question

Three answers, and this document has no preference it would defend:

1. **Re-cut for Jellyfin at its default settings.** Timeline previews appear for
   most people; they are wrong or absent for anybody who changed `Interval`,
   `WidthResolutions`, `TileWidth` or `TileHeight`, with nothing telling them
   why. It accepts the resampling loss and a per-file `ffmpeg` pass.
2. **Drop the Library scope.** User previews stay on this tool's own surfaces —
   the Preview gallery and the Review Queue — and nothing is written beside a
   Video File.
3. **Read the server's settings.** Let this tool ask a Jellyfin server for its
   trickplay configuration so the re-cut is correct rather than assumed. That is
   a media-server connection this tool does not have, and it would be a decision
   of its own.

The first ships something most people see and quietly misleads a minority; the
second is honest and delivers nothing here; the third is right and is larger
than the work this investigation belongs to.

## Reproducing it

The whole run, from an empty directory. Docker and `ffmpeg` are the only
requirements.

```sh
mkdir -p jf/{config,cache} "jf/media/Movies/Timecode Scene (2026)"
cd jf
D="media/Movies/Timecode Scene (2026)"

# A video whose every frame says what second it is.
ffmpeg -y -f lavfi -i "color=c=black:s=640x360:d=120:r=10" \
  -vf "drawtext=text='%{eif\:t\:d}':fontsize=120:fontcolor=white:x=(w-text_w)/2:y=(h-text_h)/2" \
  -c:v libx264 -pix_fmt yuv420p -g 10 "$D/Timecode Scene (2026).mkv"

# A sheet in the shape prdb publishes one: 12 tiles at 10 s, 4x3 of 320x180.
ffmpeg -y -i "$D/Timecode Scene (2026).mkv" \
  -vf "fps=1/10,scale=320:180,tile=4x3" -frames:v 1 sprite.jpg

# The two layouts, tested together.
cp sprite.jpg "$D/Timecode Scene (2026)-sprite.jpg"         # the naive sidecar
# …and a WebVTT beside it as "Timecode Scene (2026).vtt"     # the harmful half
mkdir -p "$D/Timecode Scene (2026).trickplay/320 - 10x10"
cp sprite.jpg "$D/Timecode Scene (2026).trickplay/320 - 10x10/0.jpg"

docker run -d --name jf -p 18096:8096 \
  -v "$PWD/config:/config" -v "$PWD/cache:/cache" -v "$PWD/media:/media" \
  jellyfin/jellyfin:10.11.11
```

Then, through the API: complete the startup wizard, add `/media/Movies` as a
movie library, set the library's `SaveTrickplayWithMedia`,
`EnableTrickplayImageExtraction` and `ExtractTrickplayImagesDuringLibraryScan`,
leave `TrickplayOptions` at its defaults, scan, and read

- `GET /Items?recursive=true&includeItemTypes=Movie&fields=Trickplay` for what
  the server made of the sheet,
- `GET /Videos/{id}/Trickplay/320/tiles.m3u8` for what a client is told,
- `GET /Items/{id}?fields=MediaSources` for what became of the WebVTT.

The control is the same video in a second folder with no trickplay directory.
