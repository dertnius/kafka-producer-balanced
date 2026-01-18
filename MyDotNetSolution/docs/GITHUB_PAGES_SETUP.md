# GitHub Pages Setup Guide

## Option 1: Using GitHub Pages (Recommended) ✅

### Step 1: Create GitHub Repository

1. Go to [GitHub](https://github.com/new)
2. Create a new repository: `kafka-producer-balanced`
3. Clone it to your machine

### Step 2: Add Your Code to GitHub

```bash
cd C:\Users\midgard\dev\kafka-producer-balanced\MyDotNetSolution
git remote add origin https://github.com/YOUR_USERNAME/kafka-producer-balanced.git
git branch -M main
git push -u origin main
```

### Step 3: Enable GitHub Pages

1. Go to your repository on GitHub
2. Click **Settings** → **Pages**
3. Under **Build and deployment**:
   - **Source**: Deploy from a branch
   - **Branch**: `main` / folder: `/(root)` or `/docs`
   - Click **Save**

### Step 4: Update repository.md in docs folder

Ensure your `docs/` folder contains:
```
docs/
├── index.html          ✅ (already created)
├── _sidebar.md         ✅ (already created)
├── _navbar.md          ✅ (already created)
├── _coverpage.md       ✅ (already created)
├── README.md           (copy from root or create)
├── MEMORY_*.md         (copy all memory docs here)
├── PRODUCTION_*.md     (copy all production docs here)
└── ... (other markdown files)
```

### Step 5: Copy Markdown Files to docs/

```powershell
# PowerShell - copy all markdown files to docs/
$markdownFiles = @(
    'README.md',
    'QUICK_REFERENCE.md',
    'PRODUCTION_READINESS.md',
    'MEMORY_VISUAL_GUIDE.md',
    'MEMORY_QUICK_START.md',
    'MEMORY_TRACKING_EXAMPLES.md',
    'MEMORY_TRACKING.md',
    'MEMORY_TRACKING_IMPLEMENTATION.md',
    'MEMORY_COMPLETE.md',
    'MEMORY_LEAK_RESOLUTION.md',
    'PUBLISHING_FEATURE_GUIDE.md',
    'PUBLISHING_IMPLEMENTATION.md',
    'README_PUBLISHING.md',
    'PRODUCTION_GUIDE.md',
    'DEPLOYMENT_CHECKLIST.md',
    'PERFORMANCE_ANALYSIS.md',
    'DOCUMENTATION_INDEX.md',
    'IMPLEMENTATION_SUMMARY.md',
    'COMPLETION_SUMMARY.md',
    'DATABASE_SCHEMA.sql',
    'mermaid.md'
)

foreach ($file in $markdownFiles) {
    if (Test-Path $_) {
        Copy-Item $_ -Destination docs/ -Force
    }
}
```

### Step 6: Commit and Push

```bash
git add docs/
git commit -m "Add Docsify documentation site"
git push origin main
```

### Step 7: View Your Site

Your documentation will be available at:
```
https://YOUR_USERNAME.github.io/kafka-producer-balanced/
```

---

## Option 2: Using /docs Folder on main Branch

If you see build errors, make sure GitHub Pages is configured to use the `/docs` folder:

1. Settings → Pages
2. Select **main branch** → **/docs folder**
3. Click **Save**

---

## Option 3: Using gh-pages Branch

For advanced setup:

```bash
# Create gh-pages branch
git checkout -b gh-pages

# Copy docs content
git add docs/
git commit -m "Deploy documentation"
git push origin gh-pages

# Set GitHub Pages to use gh-pages branch
# Settings → Pages → Select gh-pages branch
```

---

## Verify Your Setup

After pushing, check:

```bash
# Check that files are in docs/ folder
ls -la docs/

# Verify index.html exists
file docs/index.html

# Verify markdown files exist
ls -la docs/*.md
```

---

## Customize Docsify Theme

### Change Theme Color

Edit `docs/index.html` and modify:

```javascript
window.$docsify = {
  // ... other options
  themeColor: '#2c5aa0',  // Change this hex color
}
```

### Available Themes

- `//cdn.jsdelivr.net/npm/docsify@4/lib/themes/vue.css` (default)
- `//cdn.jsdelivr.net/npm/docsify@4/lib/themes/buble.css`
- `//cdn.jsdelivr.net/npm/docsify@4/lib/themes/dark.css`
- `//cdn.jsdelivr.net/npm/docsify@4/lib/themes/pure.css`

---

## Troubleshooting

### My site shows 404

- ✅ Ensure `index.html` is in the `docs/` folder
- ✅ Make sure GitHub Pages is enabled in Settings
- ✅ Wait 2-3 minutes for GitHub to build the site
- ✅ Check if you chose the correct folder (root vs /docs)

### Markdown files not showing

- ✅ Copy all `.md` files to `docs/` folder
- ✅ Update paths in `_sidebar.md` to match
- ✅ Refresh browser (Ctrl+Shift+Del to clear cache)

### Broken navigation links

- ✅ Update repository URL in `index.html`
- ✅ Check `_sidebar.md` and `_navbar.md` file paths
- ✅ Use relative paths like `README.md` not `./README.md`

---

## Advanced: Add Search Bar

Already included! Search works automatically for:
- Page titles
- Headers
- Content text

---

## Next Steps

1. ✅ Copy all markdown files to `docs/`
2. ✅ Update GitHub repository URL in `docs/index.html`
3. ✅ Push to GitHub
4. ✅ Enable GitHub Pages in Settings
5. ✅ Visit `https://yourusername.github.io/kafka-producer-balanced/`

Enjoy your professional documentation site! 🎉
