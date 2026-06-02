# Poster Optimization Guide

To optimize large PNG files in this directory without running multiple individual commands, we use the `optimize.py` script run via `run.cmd`.

## Setup

- **`optimize.py`**: A Python script using Pillow (`PIL`) to open `posterAlt.png`, automatically restore it from `.bak` if it exists, resize the image so that the longest side is 1024px (maintaining aspect ratio), check if the alpha channel is fully opaque (converting to `RGB` to save space if it is), and then compress and save the image with maximum compression settings.
- **`run.cmd`**: A batch file located in [scripts/run.cmd](file:///E:/+%20Worlds%20Archive/%5BHUB%5D/Assets/FoxDenGitHub/scripts/run.cmd) that executes the python script and pauses to allow reading the log output.

## How to Optimize Again or Target Different Files

If you need to optimize another image or run this again:

1. **Change the target filename**: Edit [optimize.py](file:///E:/+%20Worlds%20Archive/%5BHUB%5D/Assets/FoxDenGitHub/posters/optimize.py) and update the `img_path` variable to point to the desired image.
2. **Execute the batch script**: Run [run.cmd](file:///E:/+%20Worlds%20Archive/%5BHUB%5D/Assets/FoxDenGitHub/scripts/run.cmd).
