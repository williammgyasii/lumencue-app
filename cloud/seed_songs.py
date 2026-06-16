"""
Seeds the CEYC test organization with REAL worship/Christian song lyrics pulled from the
open LRCLIB API (https://lrclib.net), so the smart song search can be exercised against
authentic content.

What it does:
  1. Fetches plain lyrics for a curated list of well-known worship songs from LRCLIB.
  2. Splits each into sections (honoring explicit [Verse]/[Chorus] headers, otherwise
     auto-detecting repeated stanzas as the chorus) — mirroring the app's SongImportParser
     so seeded songs match app-created ones.
  3. Tombstones the earlier hand-entered hymn seeds in the org (by title), preserving any
     other songs (e.g. the user's own "Number One").
  4. Publishes the fetched songs to the cloud via the LumenCue API; the desktop pulls them on sync.

Run with the LumenCue API listening on localhost:5080:
    python cloud/seed_songs.py

Note: lyrics are written straight to the database and are never printed to the console.
"""

import json
import re
import time
import urllib.parse
import urllib.request
import uuid

API = "http://localhost:5080"
ORG = "ceyc-airport"
LRCLIB = "https://lrclib.net"
UA = "LumenCue/0.1 (+https://lumencue.app)"

# Curated worship/Christian songs (title, artist) likely present in LRCLIB.
WANTED = [
    ("Amazing Grace (My Chains Are Gone)", "Chris Tomlin"),
    ("How Great Is Our God", "Chris Tomlin"),
    ("Good Good Father", "Chris Tomlin"),
    ("10,000 Reasons (Bless the Lord)", "Matt Redman"),
    ("Blessed Be Your Name", "Matt Redman"),
    ("What a Beautiful Name", "Hillsong Worship"),
    ("Who You Say I Am", "Hillsong Worship"),
    ("King of Kings", "Hillsong Worship"),
    ("Cornerstone", "Hillsong Worship"),
    ("Mighty to Save", "Hillsong Worship"),
    ("O Praise the Name (Anastasis)", "Hillsong Worship"),
    ("Oceans (Where Feet May Fail)", "Hillsong United"),
    ("Another in the Fire", "Hillsong United"),
    ("So Will I (100 Billion X)", "Hillsong United"),
    ("Good Grace", "Hillsong United"),
    ("Goodness of God", "Bethel Music"),
    ("Raise a Hallelujah", "Bethel Music"),
    ("No Longer Slaves", "Bethel Music"),
    ("It Is Well", "Bethel Music"),
    ("Reckless Love", "Cory Asbury"),
    ("Living Hope", "Phil Wickham"),
    ("This Is Amazing Grace", "Phil Wickham"),
    ("Great Things", "Phil Wickham"),
    ("House of the Lord", "Phil Wickham"),
    ("Battle Belongs", "Phil Wickham"),
    ("Build My Life", "Pat Barrett"),
    ("Great Are You Lord", "All Sons & Daughters"),
    ("The Blessing", "Kari Jobe"),
    ("Forever (We Sing Hallelujah)", "Kari Jobe"),
    ("Graves Into Gardens", "Elevation Worship"),
    ("Do It Again", "Elevation Worship"),
    ("O Come to the Altar", "Elevation Worship"),
    ("Same God", "Elevation Worship"),
    ("The Lion and the Lamb", "Leeland"),
    ("In Christ Alone", "Keith & Kristyn Getty"),
    ("Way Maker", "Sinach"),
    ("Here I Am to Worship", "Tim Hughes"),
    ("Shout to the Lord", "Darlene Zschech"),
    ("Lord I Need You", "Matt Maher"),
    ("Death Was Arrested", "North Point Worship"),
    ("Tremble", "Mosaic MSC"),
    ("Gratitude", "Brandon Lake"),
    ("Promises", "Maverick City Music"),
    ("Jireh", "Maverick City Music"),
    ("Firm Foundation (He Won't)", "Cody Carnes"),
    ("Run to the Father", "Cody Carnes"),
    ("Holy Spirit", "Francesca Battistelli"),
    ("Yes I Will", "Vertical Worship"),
    ("Build Your Kingdom Here", "Rend Collective"),
    ("How He Loves", "David Crowder"),
    ("Come As You Are", "Crowder"),
    ("Man of Sorrows", "Hillsong Worship"),
    ("Champion", "Bethel Music"),
    ("Egypt", "Cory Asbury"),
]

# Titles published by the earlier hand-entered hymn seed — tombstoned so they don't linger.
OLD_SEED_TITLES = {
    "Amazing Grace", "Holy, Holy, Holy", "Blessed Assurance", "It Is Well with My Soul",
    "Be Thou My Vision", "Come Thou Fount of Every Blessing", "Crown Him with Many Crowns",
    "To God Be the Glory", "What a Friend We Have in Jesus", "Rock of Ages",
    "When I Survey the Wondrous Cross", "O for a Thousand Tongues to Sing",
    "Joyful, Joyful We Adore Thee", "All Hail the Power of Jesus' Name",
    "Praise to the Lord, the Almighty", "A Mighty Fortress Is Our God",
    "'Tis So Sweet to Trust in Jesus", "I Surrender All", "Just As I Am",
    "Leaning on the Everlasting Arms", "Standing on the Promises", "Nothing but the Blood",
    "Power in the Blood", "Blessed Be the Name", "Great Is Thy Faithfulness",
    "Turn Your Eyes Upon Jesus", "The Old Rugged Cross", "Victory in Jesus",
    "Sweet Hour of Prayer", "Come Thou Almighty King", "Fairest Lord Jesus",
    "My Faith Looks Up to Thee", "I Need Thee Every Hour", "Take My Life and Let It Be",
    "Tell Me the Old, Old Story", "Trust and Obey", "Wonderful Words of Life",
    "Pass Me Not, O Gentle Savior", "Praise Him! Praise Him!", "Onward Christian Soldiers",
    "Stand Up, Stand Up for Jesus", "Love Divine, All Loves Excelling", "Jesus Paid It All",
    "There Is a Fountain", "Softly and Tenderly", "Count Your Blessings", "He Hideth My Soul",
    "Near the Cross", "Holy Spirit, Truth Divine", "Come, Ye Thankful People, Come",
    "My Hope Is Built on Nothing Less",
}

HEADER_RE = re.compile(
    r"^\s*[\[\(]?\s*(verse\s*\d*|chorus|bridge|pre[\s-]?chorus|tag|outro|intro|refrain|interlude|vamp|ending|hook)\s*\d*\s*[\]\)]?\s*:?\s*$",
    re.IGNORECASE,
)
NON_ALNUM = re.compile(r"[^a-z0-9]+")

MAX_VERSE_LINES = 8
FALLBACK_CHUNK = 4


def normalize_type(raw):
    l = raw.strip().lower()
    if l.startswith("verse"):
        return "verse"
    if "pre" in l and "chorus" in l:
        return "pre-chorus"
    if l.startswith("chorus") or l.startswith("refrain") or l.startswith("hook"):
        return "chorus"
    if l.startswith("bridge"):
        return "bridge"
    if l.startswith("tag"):
        return "tag"
    if l.startswith("outro") or l.startswith("ending"):
        return "outro"
    if l.startswith("intro"):
        return "intro"
    return "verse"


def norm_key(text):
    return NON_ALNUM.sub(" ", text.lower()).strip()


def _chunk(lines, size=FALLBACK_CHUNK):
    return ["\n".join(lines[i:i + size]) for i in range(0, len(lines), size)]


def build_sections(plain):
    """Returns a list of (section_type, text) preserving order."""
    text = plain.replace("\r\n", "\n").replace("\r", "\n").strip()
    if not text:
        return []

    lines_all = text.split("\n")
    has_header = any(HEADER_RE.match(l) for l in lines_all)

    typed_blocks = []  # (type or None, body)
    if has_header:
        cur_type = "verse"
        cur = []
        for raw in lines_all:
            line = raw.rstrip()
            if HEADER_RE.match(line):
                if cur:
                    typed_blocks.append((cur_type, "\n".join(cur)))
                    cur = []
                cur_type = normalize_type(HEADER_RE.match(line).group(1))
            elif line.strip():
                cur.append(line.strip())
            elif cur:
                typed_blocks.append((cur_type, "\n".join(cur)))
                cur = []
                cur_type = "verse"
        if cur:
            typed_blocks.append((cur_type, "\n".join(cur)))
    else:
        raw_blocks = [b.strip() for b in re.split(r"\n\s*\n", text) if b.strip()]
        if len(raw_blocks) <= 1:
            # Single solid block: chunk evenly so a song isn't one giant slide.
            flat = [l.strip() for l in text.split("\n") if l.strip()]
            raw_blocks = _chunk(flat) if len(flat) > FALLBACK_CHUNK else (["\n".join(flat)] if flat else [])
        counts = {}
        for b in raw_blocks:
            counts[norm_key(b)] = counts.get(norm_key(b), 0) + 1
        for b in raw_blocks:
            typed_blocks.append(("chorus" if counts[norm_key(b)] >= 2 else None, b))

    # Resolve None types to verse, and sub-divide overly long verse blocks.
    resolved = []
    for t, body in typed_blocks:
        body_lines = [l for l in body.split("\n") if l.strip()]
        if not body_lines:
            continue
        t = t or "verse"
        if t == "verse" and len(body_lines) > MAX_VERSE_LINES:
            for chunk in _chunk(body_lines):
                resolved.append(("verse", chunk))
        else:
            resolved.append((t, "\n".join(body_lines)))
    return resolved


def to_payload_sections(plain):
    """Mirror SongImportParser.CreateSection numbering: verses sequential, others by global order."""
    out = []
    verse_count = 0
    order = 0
    for sectype, body in build_sections(plain):
        if sectype == "verse":
            verse_count += 1
            section_order = verse_count
        else:
            section_order = order + 1
        order += 1
        out.append({"sectionType": sectype, "sectionOrder": section_order, "text": body})
    return out


def http_get_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=25) as resp:
        return json.loads(resp.read())


def score(result, title, artist):
    tn = norm_key(result.get("trackName") or "")
    an = norm_key(result.get("artistName") or "")
    s = 0
    title_core = norm_key(re.sub(r"\(.*?\)", "", title))
    if title_core and title_core in tn:
        s += 3
    if norm_key(artist) and norm_key(artist).split()[0] in an:
        s += 2
    if result.get("plainLyrics"):
        s += 2
    if result.get("instrumental"):
        s -= 5
    return s


def fetch_lyrics(title, artist):
    qs = urllib.parse.urlencode({"track_name": title, "artist_name": artist})
    try:
        results = http_get_json(f"{LRCLIB}/api/search?{qs}")
    except Exception:
        results = []
    if not results:
        # Broader fallback search across all fields.
        try:
            results = http_get_json(f"{LRCLIB}/api/search?{urllib.parse.urlencode({'q': f'{title} {artist}'})}")
        except Exception:
            results = []
    best, best_s = None, 0
    for r in results or []:
        sc = score(r, title, artist)
        if sc > best_s and r.get("plainLyrics"):
            best, best_s = r, sc
    return best


def get_current_songs():
    try:
        body = http_get_json(f"{API}/orgs/{ORG}/songs")
        return body.get("changed", [])
    except Exception:
        return []


def put_songs(songs):
    data = json.dumps(songs).encode("utf-8")
    req = urllib.request.Request(
        f"{API}/orgs/{ORG}/songs", data=data, method="PUT",
        headers={"Content-Type": "application/json", "User-Agent": UA},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.status


def main():
    # 1) Fetch real lyrics.
    new_songs = []
    misses = []
    for title, artist in WANTED:
        hit = fetch_lyrics(title, artist)
        if not hit:
            misses.append(title)
            print(f"  miss   {title}")
            time.sleep(0.15)
            continue
        sections = to_payload_sections(hit.get("plainLyrics") or "")
        if not sections:
            misses.append(title)
            print(f"  empty  {title}")
            time.sleep(0.15)
            continue
        new_songs.append({
            "cloudId": str(uuid.uuid4()),
            "title": hit.get("trackName") or title,
            "artist": hit.get("artistName") or artist,
            "linesPerSlide": 0,
            "organizationId": ORG,
            "deleted": False,
            "sections": sections,
        })
        print(f"  ok     {hit.get('trackName') or title}  ({len(sections)} sections)")
        time.sleep(0.15)

    # 2) Tombstone the old hand-entered hymn seeds.
    tombstones = []
    for s in get_current_songs():
        if s.get("title") in OLD_SEED_TITLES and not s.get("deleted") and s.get("cloudId"):
            tombstones.append({
                "cloudId": s["cloudId"], "title": s["title"], "artist": s.get("artist"),
                "linesPerSlide": s.get("linesPerSlide", 0), "organizationId": ORG,
                "deleted": True, "sections": [],
            })

    if tombstones:
        put_songs(tombstones)
        print(f"\nTombstoned {len(tombstones)} old hymn seeds.")

    # 3) Publish the new songs (in batches to keep requests small).
    for i in range(0, len(new_songs), 20):
        put_songs(new_songs[i:i + 20])
    print(f"Published {len(new_songs)} real songs from LRCLIB. Misses: {len(misses)}")

    total = len([s for s in get_current_songs() if not s.get("deleted")])
    print(f"Org '{ORG}' now has {total} active songs in the cloud.")


if __name__ == "__main__":
    main()
