import urllib.request
url = 'https://raw.githubusercontent.com/bbtarzan12/MarchingCubes/master/Assets/Scripts/MarchingCubesTables.cs'
try:
    urllib.request.urlretrieve(url, 'Assets/_Scripts/VoxelSystem/MarchingCubesTables.cs')
    print('Downloaded successfully from bbtarzan12')
except Exception as e:
    print('Failed:', e)
