"""FAISS file handles on Windows cannot consistently open Unicode filenames."""
from pathlib import Path
import faiss
import numpy as np

_native_read_index=faiss.read_index


def read_index(path):
    # Python opens the file with Windows Unicode APIs. FAISS consumes its native
    # serialized bytes without temporary files or changing the user's filename.
    reader=faiss.VectorIOReader()
    faiss.copy_array_to_vector(np.frombuffer(Path(path).read_bytes(),dtype='uint8'),reader.data)
    return _native_read_index(reader)
