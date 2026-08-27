# Recorded mount tables

`/proc/self/mountinfo`, as two real arrangements produce it.

Ticket 12 needs a case that cannot be built in a test: **two mounts of one
filesystem**. Making one means mounting something, and ADR 0042 already declined
to mount a loop device to manufacture the kernel's own cross-device refusal. So
the table is recorded and the parsing is tested against it, while the two
answers that can be reached without a mount stay as tests against real
directories in `DirectoryTests` and `LibraryRootTests`.

- `mountinfo-container.txt` — the arrangement that found the bug: a container
  whose data, downloads and library arrive as three separate bind mounts of one
  `ext4` filesystem. Three mount points, one device `9:4`. The overlay root and
  the pseudo-filesystems above the binds are kept, because they are what the
  parser has to walk past to reach the right answer.
- `mountinfo-two-devices.txt` — two paths that genuinely are on different
  devices, which is the answer the warning exists for.

The source paths in the fourth field are stand-ins. What these files are for is
the *shape* — how many mounts, which device each is on, and in which order the
kernel lists them — and the directories somebody's host happens to use are no
part of that.
