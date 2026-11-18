#!/bin/sh
# Makes assets/fonts/roboto/Roboto-Regular-subset.ttf, the test font of the raster back-end's glyph tests: a subset of
# Roboto Regular 2.138 (Apache License 2.0) with two glyphs, 'I' (U+0049, one rectangle) and 'Ɨ' (U+0197, a composite
# of 'I' and a bar: two rectangles that overlap, wound the same way, so the non-zero rule fills their union and the
# even-odd rule leaves the overlap empty). The test fonts already in the repository (Lato, Theano Didot) have no glyph
# with overlapping contours.
#
# The font comes from the Debian package fonts-roboto-unhinted 2:0~20170802-4, from the same pinned snapshot of the
# Debian archive as the reference tools image (tools/reference/tools.Dockerfile); the package and the font are checked
# against their SHA-256 before use. The subset is made with fontTools 4.57.0 (python3 -m fontTools.subset, also known as
# pyftsubset) from that image. Runs in the image as root (apt needs it) and gives the files to the calling user, from the
# family folder (libs/documents):
#
#   docker run --rm -e OWNER="$(id -u):$(id -g)" -v "$PWD:/work" -w /work ave-extensions-documents-tools:<tag> \
#       sh tools/make-roboto-subset.sh
set -eu

PACKAGE='fonts-roboto-unhinted=2:0~20170802-4'
PACKAGE_SHA256='048f1e05cfb1ad1e5cd1c283cd61f2276c6a36f75e85032ddfd65db252b4634e'
FONT_SHA256='797e35f7f5d6020a5c6ea13b42ecd668bcfb3bbc4baa0e74773527e5b6cb3174'
TARGET='assets/fonts/roboto'

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
chown _apt "$scratch"

apt-get -o Acquire::Retries=5 update -qq
(cd "$scratch" && apt-get download -qq "$PACKAGE")
echo "$PACKAGE_SHA256  $(ls "$scratch"/fonts-roboto-unhinted_*.deb)" | sha256sum -c -
dpkg-deb -x "$scratch"/fonts-roboto-unhinted_*.deb "$scratch/package"
font="$scratch/package/usr/share/fonts/truetype/roboto/unhinted/RobotoTTF/Roboto-Regular.ttf"
echo "$FONT_SHA256  $font" | sha256sum -c -

mkdir -p "$TARGET"
python3 -I -m fontTools.subset "$font" --unicodes=U+0049,U+0197 --layout-features='' --name-IDs='*' \
    --output-file="$TARGET/Roboto-Regular-subset.ttf"
cp /usr/share/common-licenses/Apache-2.0 "$TARGET/LICENSE-2.0.txt"
chown -R "${OWNER:-0:0}" "$TARGET"
sha256sum "$TARGET/Roboto-Regular-subset.ttf"
