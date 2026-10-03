<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /** Historia nadanych ramek (CONSOLE_LARAVEL.md §5.2): prawdziwe komunikaty i ataki z laboratorium. */
    public function up(): void
    {
        Schema::create('broadcasts', function (Blueprint $table) {
            $table->id();
            $table->unsignedSmallInteger('issuer_id');
            $table->json('signer_ids');
            $table->unsignedTinyInteger('type');
            $table->unsignedSmallInteger('area_code');
            $table->unsignedInteger('timestamp');
            $table->unsignedSmallInteger('valid_minutes');
            $table->unsignedSmallInteger('sequence');
            $table->string('note', 255)->default('');
            $table->text('frame_hex');
            $table->text('qr_text');
            $table->string('kind', 16)->default('genuine');        // genuine | attack
            $table->string('attack_type', 8)->nullable();          // A1..A7
            $table->timestamps();

            $table->index(['kind', 'issuer_id', 'sequence']);
        });
    }

    public function down(): void
    {
        Schema::dropIfExists('broadcasts');
    }
};
