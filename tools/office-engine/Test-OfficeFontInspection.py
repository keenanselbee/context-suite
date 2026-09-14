"""Bounded embedded-font inspection contracts; no fonts or renderers are loaded."""
import pathlib
import runpy
import struct
import unittest

INSPECTION = runpy.run_path(str(pathlib.Path(__file__).with_name('Inspect-OfficePdfComparison.py')))


def naming(records, language=None):
    strings = bytearray()
    entries = bytearray()
    for platform, encoding, language_id, name_id, data in records:
        entries.extend(struct.pack('>6H', platform, encoding, language_id, name_id, len(data), len(strings)))
        strings.extend(data)
    suffix = b''
    if language is not None:
        suffix = struct.pack('>3H', 1, len(language), len(strings))
        strings.extend(language)
    return struct.pack('>3H', int(language is not None), len(records), 6 + len(entries) + len(suffix)) + entries + suffix + strings


def font(table):
    return struct.pack('>I4H', 0x10000, 1, 16, 0, 0) + struct.pack('>4sIII', b'name', 0, 28, len(table)) + table


class FontInspection(unittest.TestCase):
    def test_unicode_and_mac_names_keep_platform_identity(self):
        data = naming([(3, 1, 0x409, 1, 'Family \u65e5'.encode('utf-16-be')),
                       (1, 0, 0, 2, 'R\u00e9gulier'.encode('mac_roman'))])
        result = INSPECTION['font_tables'](font(data))
        self.assertTrue(result['nameTablePresent'])
        self.assertEqual([name['value'] for name in result['names']['records']], ['Family \u65e5', 'R\u00e9gulier'])
        self.assertEqual(result['names']['records'][0]['language'], 0x409)

    def test_language_tag_is_separate_from_font_name(self):
        data = naming([(0, 4, 0x8000, 16, 'Family'.encode('utf-16-be'))], 'en-US'.encode('utf-16-be'))
        name = INSPECTION['font_names'](data)['records'][0]
        self.assertEqual((name['value'], name['languageTag']), ('Family', 'en-US'))

    def test_unknown_encoding_is_not_guessed(self):
        data = naming([(3, 99, 0x409, 1, b'unknown')])
        name = INSPECTION['font_names'](data)['records'][0]
        self.assertIsNone(name['value'])
        self.assertEqual(name['decoding'], 'unsupported encoding')

    def test_user_defined_platform_language_is_not_assigned_a_locale(self):
        data = naming([(240, 0, 0x8000, 1, b'custom')])
        name = INSPECTION['font_names'](data)['records'][0]
        self.assertIsNone(name['languageTag'])
        self.assertIsNone(name['value'])

    def test_unselected_names_are_not_exposed(self):
        data = naming([(3, 1, 0x409, 0, 'Private copyright declaration'.encode('utf-16-be'))])
        self.assertEqual(INSPECTION['font_names'](data)['records'], [])

    def test_missing_name_table_is_explicit(self):
        data = bytearray(font(b'1234'))
        data[12:16] = b'head'
        result = INSPECTION['font_tables'](data)
        self.assertFalse(result['nameTablePresent'])
        self.assertIsNone(result['names'])

    def test_font_directory_refusals(self):
        valid = bytearray(font(naming([(3, 1, 0x409, 1, b'\x00A')])))
        truncated = valid[:20]
        invalid_offset = valid.copy(); struct.pack_into('>I', invalid_offset, 20, 0)
        oversized = valid.copy(); struct.pack_into('>I', oversized, 24, 0xffffffff)
        for data in (b'OTTO', truncated, invalid_offset, oversized):
            with self.subTest(data=bytes(data).hex()):
                with self.assertRaises(ValueError): INSPECTION['font_tables'](data)

    def test_duplicate_and_overlapping_tables_are_refused(self):
        header = struct.pack('>I4H', 0x10000, 2, 32, 1, 0)
        for tag in (b'name', b'head'):
            data = header + struct.pack('>4sIII', b'name', 0, 44, 8) + struct.pack('>4sIII', tag, 0, 48, 4) + b'12345678'
            with self.subTest(tag=tag):
                with self.assertRaises(ValueError): INSPECTION['font_tables'](data)

    def test_naming_extents_and_invalid_unicode_are_refused(self):
        valid = bytearray(naming([(3, 1, 0x409, 1, b'\x00A')]))
        overlap = valid.copy(); struct.pack_into('>H', overlap, 4, 6)
        invalid_offset = valid.copy(); struct.pack_into('>H', invalid_offset, 16, 65535)
        bad_language = naming([(0, 4, 0x8001, 16, b'\x00A')], b'\x00e\x00n')
        odd_unicode = naming([(3, 1, 0x409, 1, b'A')])
        for data in (b'', valid[:10], overlap, invalid_offset, bad_language, odd_unicode):
            with self.subTest(data=bytes(data).hex()):
                with self.assertRaises((ValueError, UnicodeError)): INSPECTION['font_names'](data)

    def test_name_limits_and_unknown_version(self):
        shared_storage = struct.pack('>3H', 0, 17, 210) + struct.pack('>6H', 3, 1, 0x409, 1, 4096, 0) * 17 + b'\x00A' * 2048
        for data in (struct.pack('>3H', 0, 1025, 6), naming([(3, 1, 0x409, 1, b'\x00A' * 2049)]),
                     shared_storage):
            with self.assertRaises(ValueError): INSPECTION['font_names'](data)
        self.assertEqual(INSPECTION['font_names'](struct.pack('>3H', 2, 0, 6))['status'], 'unsupported naming version')


if __name__ == '__main__':
    unittest.main()
