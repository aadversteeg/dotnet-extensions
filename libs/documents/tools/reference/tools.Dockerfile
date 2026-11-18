# The reference tools that check the engine's output from the outside: qpdf, poppler (pdftotext, pdffonts, pdfimages,
# pdftoppm), MuPDF (mutool), fontTools, PyMuPDF, Pillow and libjpeg-turbo (cjpeg, djpeg). Every input is pinned, so a check never runs with other
# tools than the ones its numbers were made with:
#
# - the base image by digest (Debian 13 "trixie", slim);
# - the package archive by date: snapshot.debian.org keeps "all past and current packages the Debian archive
#   provides" in states about six hours apart, so the state of 2026-11-01 00:00 UTC resolves the same packages,
#   dependencies included, on every machine and every day (a package the service removes, which it does rarely, for
#   licence reasons, makes the build fail instead of changing a tool);
# - each tool by its exact Debian version, so an install fails instead of taking another version.
#
# Built locally, never published: tools/verify-samples.ps1, tools/reference/chromium/measure.mjs and the reference
# tests build it with "docker build -t ave-extensions-documents-tools:<first 12 hex digits of this file's SHA-256> - < tools.Dockerfile",
# so a change to this file builds a new image and an unchanged one reuses the cached image.
FROM debian@sha256:a72b4433de1cf7d6bae0c4cee74d0547f58601a643b3375f070c51600d63b6b2

RUN printf 'deb [check-valid-until=no] http://snapshot.debian.org/archive/debian/20261101T000000Z trixie main\n' > /etc/apt/sources.list \
 && rm -f /etc/apt/sources.list.d/* \
 && apt-get -o Acquire::Retries=5 update \
 && apt-get install -y --no-install-recommends \
      qpdf=12.2.0-1 \
      poppler-utils=25.03.0-5+deb13u4 \
      mupdf-tools=1.25.1+ds1-6+deb13u1 \
      python3-fonttools=4.57.0-1+deb13u1 \
      python3-pymupdf=1.25.4+ds1-3+deb13u1 \
      python3-pil=11.1.0-5+deb13u4 \
      libjpeg-turbo-progs=1:2.1.5-4 \
 && rm -rf /var/lib/apt/lists/*
