"""Create minimal Unity .meta files (stable, path-derived GUIDs) for files and folders that have none.

Why: assets added without opening the editor would otherwise get random GUIDs on every machine that imports them first,
which makes scene/prefab references and merges fragile. Unity accepts a meta that only holds a GUID and fills in the
importer settings itself on first import. Existing .meta files are never touched.

Usage: python tools/write_unity_meta.py unity/Assets/App/Forest unity/Assets/App/Relaxation/Editor
"""
import hashlib
import pathlib
import sys

FOLDER = (
    "fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
)
ASMDEF = (
    "fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n"
    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
)
PLAIN = "fileFormatVersion: 2\nguid: {guid}\n"


def guid_for(path: pathlib.Path, root: pathlib.Path) -> str:
    rel = path.relative_to(root).as_posix()
    return hashlib.md5(("vrfatigue:" + rel).encode()).hexdigest()


def write_meta(path: pathlib.Path, root: pathlib.Path) -> bool:
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return False
    template = FOLDER if path.is_dir() else ASMDEF if path.suffix == ".asmdef" else PLAIN
    meta.write_text(template.format(guid=guid_for(path, root)), encoding="utf-8", newline="\n")
    return True


def main(argv):
    if not argv:
        print(__doc__)
        return 1
    repo = pathlib.Path(__file__).resolve().parent.parent
    assets = repo / "unity"
    created = 0
    for arg in argv:
        target = (repo / arg).resolve()
        todo = [target] + sorted(target.rglob("*")) if target.is_dir() else [target]
        for path in todo:
            if path.suffix == ".meta":
                continue
            created += write_meta(path, assets)
        # parent folders up to Assets
        parent = target.parent
        while parent != assets / "Assets" and assets in parent.parents:
            created += write_meta(parent, assets)
            parent = parent.parent
    print("created", created, "meta files")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
