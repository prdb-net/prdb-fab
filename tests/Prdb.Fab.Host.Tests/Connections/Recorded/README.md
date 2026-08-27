# Recorded Newznab responses

ADR 0042 feeds Newznab from recorded real responses rather than from
hand-written fixtures, and says why: the research surveyed **five**
implementations that differ in error codes, in which fields they return, and in
whether their advertised capabilities mean anything. A hand-written fixture
tests the implementation the author imagined, which is the one the code already
works against.

What is recorded here is the **shape** — status codes, headers, XML skeletons.
Never anybody's key, and never a real download URL: the key rides in a Newznab
download URL's query string, which is the fact ADR 0037 leaned on.

| File | What it is |
|---|---|
| `caps.xml` | A capabilities document in the spec's own numbering, XXX at 6000. |
| `caps-renumbered.xml` | The same tree at different numbers, which is the case matching by name exists for. |
| `caps-nothing-here.xml` | An indexer with no category this tool searches. |
| `search.xml` | One page of a search, with a single item. |
| `unauthorized.xml` | What a wrong key answers. Byte for byte what a large real server sends. |
