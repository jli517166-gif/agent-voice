import sys
import tempfile
import unittest
import importlib.util
from pathlib import Path

if importlib.util.find_spec('faiss') is None:
    raise unittest.SkipTest('可选 RVC/FAISS 依赖未安装')

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from rvc_index import read_index


class IndexPaths(unittest.TestCase):
    def test_unicode_index_preserves_vectors_and_neighbours(self):
        import faiss
        import numpy as np
        features=np.array([[0,0,0],[1,2,3],[3,2,1]],dtype='float32')
        original=faiss.IndexFlatL2(3);original.add(features)
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'星见雅 模型.index'
            path.write_bytes(faiss.serialize_index(original).tobytes())
            from unittest.mock import patch
            # The original RVC module calls faiss.read_index, so exercise the
            # same interception as the adapter, including FAISS's nested I/O.
            with patch('faiss.read_index',side_effect=read_index):restored=faiss.read_index(str(path))
            distance,neighbours=restored.search(features,1)
            self.assertEqual(restored.ntotal,3)
            np.testing.assert_array_equal(neighbours[:,0],[0,1,2])
            np.testing.assert_allclose(distance[:,0],0,atol=1e-6)
            np.testing.assert_allclose(restored.reconstruct_n(0,3),features)
    def test_missing_index_reports_file_error(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(FileNotFoundError):read_index(Path(folder)/'不存在.index')


if __name__=='__main__':unittest.main()
