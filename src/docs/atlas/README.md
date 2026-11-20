# The X.25 Network Atlas

`index.html` is the X.25 Network Atlas: a map of the old public X.25 world, with the networks, their hosts
and their NUAs (network user addresses), and a search. It is one self-contained page. Its scripts and data
are packed inside the file, so it needs no server and nothing from the internet. Open it in a browser
straight from disk.

The docs site publishes it unchanged at `atlas/index.html`, and the site's header menu and home page link
to it (see `src/docfx/docfx.json`, the resource group for `docs/atlas`).

One change was made to the export: a "RetroX25 docs" link in the bottom right corner, back to the site's
home page. It is a few lines in the loader script, straight after
`document.documentElement.replaceWith(doc.documentElement);`, and it hangs the link off `<html>`. A link
placed in the outer page or in the page template does not survive: the loader replaces the document, and the
atlas then draws its body itself.

To update the atlas, replace `index.html` with the new export, keeping the file name. Add the link again in
the same place, then build the site again.
