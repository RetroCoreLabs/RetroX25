# The X.25 Network Atlas

`index.html` is the X.25 Network Atlas: a map of the old public X.25 world, with the networks, their hosts
and their NUAs (network user addresses), and a search. It is one self-contained page. Its scripts and data
are packed inside the file, so it needs no server and nothing from the internet. Open it in a browser
straight from disk.

The docs site publishes it unchanged at `atlas/index.html`, and the site's header menu and home page link
to it (see `src/docfx/docfx.json`, the resource group for `docs/atlas`).

To update it, replace `index.html` with the new export, keeping the file name, and build the site again.
