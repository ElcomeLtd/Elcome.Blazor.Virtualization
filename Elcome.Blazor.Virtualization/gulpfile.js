/// <binding BeforeBuild='sass' ProjectOpened='default' />
'use strict'

const gulp = require("gulp");
const sourcemaps = require('gulp-sourcemaps');
const terser = require("gulp-terser");
const fs = require('fs');
const path = require('path');
const source = require("vinyl-source-stream");
const buffer = require('vinyl-buffer');
const rollupStream = require("@rollup/stream");
const typescript = require("@rollup/plugin-typescript");

const globs = {
    tsWatch: ["wwwroot/ts/**/*.ts"],
};

function watch(cb) {
    let watches = [];
    watches.push(gulp.watch(globs.tsWatch, compileTs));
    // Terminate if package-lock changes
    watches.push(gulp.watch('./package-lock.json', function (cb2) {
        console.log("package-lock.json changed");
        watches.forEach(watch => { watch.close(); });
        cb2();
        cb();
    }));
}

// Checks whether "npm ci" needs to be run
function checkNpm(cb) {
    let packageLock = JSON.parse(fs.readFileSync('./package-lock.json', 'utf8'));
    let success = true;
    Object.entries(packageLock.packages).forEach(([modulePath, expectedPackage]) => {
        const packagePath = path.join(modulePath, 'package.json');
        if (fs.existsSync(packagePath)) {
            let foundPackage = JSON.parse(fs.readFileSync(packagePath, 'utf8'));
            if (foundPackage.version != expectedPackage.version) {
                console.error(`Package version mismatch for ${modulePath}. Expected ${expectedPackage.version}, found ${foundPackage.version}`);
                success = false;
            }
        } else if (!expectedPackage.optional) {
            console.error(`Package ${modulePath} is missing`);
            success = false;
        }
    });
    if (success) {
        cb();
    } else {
        cb(new Error(`NPM package errors - run "npm ci" to resolve`));
    }
    return undefined;
}

let cache;

function compileTs() {
    return rollupStream({
        input: "wwwroot/ts/bundle.ts",
        cache,
        output: {
            format: "es",
            sourcemap: "inline",
        },
        plugins: [typescript()],
    })
        .on("bundle", (bundle) => {
            // update the cache after every new bundle is created
            cache = bundle;
        })
        .pipe(source("bundle.ts.js"))
        .pipe(buffer())
        .pipe(sourcemaps.init({
            loadMaps: true,
        }))
        .pipe(terser({
            module: true,
        }))
        .pipe(sourcemaps.write('.'))
        .pipe(gulp.dest("wwwroot/ts"));
}

exports.compileTs = gulp.series(checkNpm, compileTs);
exports.watch = gulp.series(checkNpm, compileTs, watch);
exports.default = exports.watch;
