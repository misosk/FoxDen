import os
from PIL import Image

def main():
    img_path = r"E:\+ Worlds Archive\[HUB]\Assets\FoxDenGitHub\posters\posterAlt.png"
    temp_path = img_path + ".tmp"
    
    backup_path = img_path + ".bak"
    if os.path.exists(backup_path):
        print("Backup found. Restoring original high-resolution file first...")
        try:
            if os.path.exists(img_path):
                os.remove(img_path)
            os.rename(backup_path, img_path)
        except Exception as e:
            print(f"Error restoring backup: {e}")

    if not os.path.exists(img_path):
        print(f"Error: {img_path} does not exist.")
        return

    print(f"Opening {img_path}...")
    img = Image.open(img_path)
    print(f"Format: {img.format}, Size: {img.size}, Mode: {img.mode}")
    
    # Resize so that the longest side is 1024px
    max_side = 1024
    if max(img.size) > max_side:
        print(f"Resizing image. Current size: {img.size}")
        img.thumbnail((max_side, max_side), Image.Resampling.LANCZOS)
        print(f"New size: {img.size}")
    
    # Check if alpha is used
    if img.mode == 'RGBA':
        print("Checking if alpha channel is used...")
        r, g, b, a = img.split()
        min_alpha, max_alpha = a.getextrema()
        print(f"Alpha range: {min_alpha} to {max_alpha}")
        if min_alpha == 255:
            print("Alpha channel is fully opaque. Converting to RGB...")
            img = img.convert('RGB')
        else:
            print("Alpha channel contains transparent/semi-transparent pixels. Keeping RGBA.")
            
    print("Saving optimized image...")
    img.save(temp_path, format="PNG", optimize=True, compress_level=9)
    
    orig_size = os.path.getsize(img_path)
    new_size = os.path.getsize(temp_path)
    print(f"Original size: {orig_size:,} bytes")
    print(f"Optimized size: {new_size:,} bytes")
    
    if new_size < orig_size:
        print("Optimization successful. Replacing original file...")
        backup_path = img_path + ".bak"
        if os.path.exists(backup_path):
            try:
                os.remove(backup_path)
            except Exception as e:
                print(f"Could not remove old backup: {e}")
        try:
            os.rename(img_path, backup_path)
            os.rename(temp_path, img_path)
            print("Done! Replaced original file. Backup saved as .bak")
        except Exception as e:
            print(f"Error swapping files: {e}")
            if os.path.exists(temp_path):
                os.remove(temp_path)
    else:
        print("Optimized file is not smaller than original. Leaving original file untouched.")
        if os.path.exists(temp_path):
            os.remove(temp_path)

if __name__ == "__main__":
    main()
